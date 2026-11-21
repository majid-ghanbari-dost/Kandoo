using System.Security.Cryptography;
using Npgsql;

namespace Kandoo.Cloud;

public sealed class PostgresCustomerAccountStore : ICustomerAccountStore
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS customer_otp_challenges (
            challenge_id uuid PRIMARY KEY,
            operation_id text NOT NULL UNIQUE,
            phone text NOT NULL,
            request_fingerprint text NOT NULL,
            code_hash text NOT NULL,
            created_at timestamptz NOT NULL,
            expires_at timestamptz NOT NULL,
            attempt_count integer NOT NULL,
            provider_message_id text NULL,
            sent_at timestamptz NULL,
            consumed_at timestamptz NULL
        );
        CREATE TABLE IF NOT EXISTS customer_otp_rate (
            phone text PRIMARY KEY,
            window_start timestamptz NOT NULL,
            issue_count integer NOT NULL
        );
        CREATE TABLE IF NOT EXISTS customer_accounts (
            account_id uuid PRIMARY KEY,
            verified_phone text NOT NULL UNIQUE,
            verified_at timestamptz NOT NULL
        );
        CREATE TABLE IF NOT EXISTS customer_sessions (
            token_hash text PRIMARY KEY,
            account_id uuid NOT NULL REFERENCES customer_accounts(account_id) ON DELETE CASCADE,
            created_at timestamptz NOT NULL,
            expires_at timestamptz NOT NULL
        );
        CREATE TABLE IF NOT EXISTS customer_invoice_claims (
            account_id uuid NOT NULL REFERENCES customer_accounts(account_id) ON DELETE CASCADE,
            store_id uuid NOT NULL,
            invoice_id uuid NOT NULL,
            public_token text NOT NULL UNIQUE REFERENCES public_invoices(public_token) ON DELETE CASCADE,
            claimed_at timestamptz NOT NULL,
            PRIMARY KEY (account_id, store_id, invoice_id)
        );
        """;

    private readonly string _connectionString;
    private readonly PostgresCloudStore _cloudStore;
    private readonly object _schemaSync = new();
    private bool _schemaReady;

    public PostgresCustomerAccountStore(string connectionString, PostgresCloudStore cloudStore)
    {
        _connectionString = connectionString;
        _cloudStore = cloudStore;
    }

    public CustomerOtpReservation ReserveOtpIssue(
        string phone,
        string operationId,
        string requestFingerprint,
        Guid challengeId,
        string codeHash,
        DateTimeOffset now,
        CustomerOtpPolicy policy)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var command = new NpgsqlCommand(
                   "SELECT challenge_id, request_fingerprint, expires_at, sent_at FROM customer_otp_challenges WHERE operation_id = @operation FOR UPDATE",
                   connection, transaction))
        {
            command.Parameters.AddWithValue("operation", operationId);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                var result = reader.GetString(1) == requestFingerprint
                    ? reader.IsDBNull(3)
                        ? new CustomerOtpReservation("DELIVERY_INCOMPLETE", reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(2))
                        : new CustomerOtpReservation("DUPLICATE", reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(2))
                    : new CustomerOtpReservation("OPERATION_ID_CONFLICT", reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(2));
                reader.Close();
                transaction.Commit();
                return result;
            }
        }

        var windowStart = now;
        var issueCount = 0;
        using (var command = new NpgsqlCommand("""
            INSERT INTO customer_otp_rate (phone, window_start, issue_count) VALUES (@phone, @window, 0)
            ON CONFLICT (phone) DO UPDATE SET phone = EXCLUDED.phone
            RETURNING window_start, issue_count;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("phone", phone);
            command.Parameters.AddWithValue("window", now);
            using var reader = command.ExecuteReader();
            reader.Read();
            windowStart = reader.GetFieldValue<DateTimeOffset>(0);
            issueCount = reader.GetInt32(1);
        }

        if (windowStart.AddSeconds(policy.RateWindowSeconds) <= now)
        {
            windowStart = now;
            issueCount = 0;
        }

        if (issueCount >= policy.IssuesPerWindow)
        {
            var retry = Math.Max(1, (int)Math.Ceiling((windowStart.AddSeconds(policy.RateWindowSeconds) - now).TotalMilliseconds));
            transaction.Commit();
            return new CustomerOtpReservation("RATE_LIMITED", challengeId, now.AddSeconds(policy.CodeLifetimeSeconds), retry);
        }

        using (var command = new NpgsqlCommand("""
            INSERT INTO customer_otp_challenges
                (challenge_id, operation_id, phone, request_fingerprint, code_hash, created_at, expires_at, attempt_count)
            VALUES (@id, @operation, @phone, @fingerprint, @hash, @created, @expires, 0);
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", challengeId);
            command.Parameters.AddWithValue("operation", operationId);
            command.Parameters.AddWithValue("phone", phone);
            command.Parameters.AddWithValue("fingerprint", requestFingerprint);
            command.Parameters.AddWithValue("hash", codeHash);
            command.Parameters.AddWithValue("created", now);
            command.Parameters.AddWithValue("expires", now.AddSeconds(policy.CodeLifetimeSeconds));
            command.ExecuteNonQuery();
        }

        using (var command = new NpgsqlCommand(
                   "UPDATE customer_otp_rate SET window_start = @window, issue_count = @count WHERE phone = @phone",
                   connection, transaction))
        {
            command.Parameters.AddWithValue("phone", phone);
            command.Parameters.AddWithValue("window", windowStart);
            command.Parameters.AddWithValue("count", issueCount + 1);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return new CustomerOtpReservation("RESERVED", challengeId, now.AddSeconds(policy.CodeLifetimeSeconds));
    }

    public void CompleteOtpIssue(Guid challengeId, string providerMessageId, DateTimeOffset sentAt)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            "UPDATE customer_otp_challenges SET provider_message_id = @provider, sent_at = @sent WHERE challenge_id = @id AND sent_at IS NULL",
            connection);
        command.Parameters.AddWithValue("id", challengeId);
        command.Parameters.AddWithValue("provider", providerMessageId);
        command.Parameters.AddWithValue("sent", sentAt);
        command.ExecuteNonQuery();
    }

    public CustomerAccountResult VerifyOtp(Guid challengeId, string code, DateTimeOffset now, CustomerOtpPolicy policy)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        string phone;
        string storedHash;
        DateTimeOffset expiresAt;
        int attempts;
        bool consumed;
        using (var command = new NpgsqlCommand("""
            SELECT phone, code_hash, expires_at, attempt_count, consumed_at
            FROM customer_otp_challenges WHERE challenge_id = @id FOR UPDATE;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", challengeId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return CustomerAccountResult.Failure(400, "OTP_INVALID", "The verification challenge is invalid.", "REAUTHENTICATE");
            }

            phone = reader.GetString(0);
            storedHash = reader.GetString(1);
            expiresAt = reader.GetFieldValue<DateTimeOffset>(2);
            attempts = reader.GetInt32(3);
            consumed = !reader.IsDBNull(4);
        }

        if (consumed)
        {
            return CustomerAccountResult.Failure(409, "OTP_ALREADY_VERIFIED", "The challenge was already consumed.", "REAUTHENTICATE");
        }

        if (now >= expiresAt)
        {
            return CustomerAccountResult.Failure(410, "OTP_EXPIRED", "The verification challenge expired.", "REAUTHENTICATE");
        }

        if (attempts >= policy.MaximumAttempts)
        {
            return CustomerAccountResult.Failure(429, "OTP_ATTEMPTS_EXCEEDED", "Too many verification attempts were made.", "REAUTHENTICATE");
        }

        var suppliedHash = CustomerAccountService.HashCode(challengeId, code);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(storedHash), Convert.FromHexString(suppliedHash)))
        {
            attempts++;
            using var update = new NpgsqlCommand(
                "UPDATE customer_otp_challenges SET attempt_count = @attempts WHERE challenge_id = @id", connection, transaction);
            update.Parameters.AddWithValue("id", challengeId);
            update.Parameters.AddWithValue("attempts", attempts);
            update.ExecuteNonQuery();
            transaction.Commit();
            return attempts >= policy.MaximumAttempts
                ? CustomerAccountResult.Failure(429, "OTP_ATTEMPTS_EXCEEDED", "Too many verification attempts were made.", "REAUTHENTICATE")
                : CustomerAccountResult.Failure(400, "OTP_INVALID", "The verification code is invalid.", "FIX_REQUEST", ("attempts_remaining", policy.MaximumAttempts - attempts));
        }

        Guid accountId;
        DateTimeOffset verifiedAt;
        using (var command = new NpgsqlCommand("""
            INSERT INTO customer_accounts (account_id, verified_phone, verified_at) VALUES (@id, @phone, @verified)
            ON CONFLICT (verified_phone) DO UPDATE SET verified_phone = EXCLUDED.verified_phone
            RETURNING account_id, verified_at;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("phone", phone);
            command.Parameters.AddWithValue("verified", now);
            using var reader = command.ExecuteReader();
            reader.Read();
            accountId = reader.GetGuid(0);
            verifiedAt = reader.GetFieldValue<DateTimeOffset>(1);
        }

        var accessToken = CustomerAccountService.NewAccessToken();
        var sessionExpiry = now.AddSeconds(policy.SessionLifetimeSeconds);
        using (var command = new NpgsqlCommand(
                   "INSERT INTO customer_sessions (token_hash, account_id, created_at, expires_at) VALUES (@hash, @account, @created, @expires)",
                   connection, transaction))
        {
            command.Parameters.AddWithValue("hash", CustomerAccountService.HashToken(accessToken));
            command.Parameters.AddWithValue("account", accountId);
            command.Parameters.AddWithValue("created", now);
            command.Parameters.AddWithValue("expires", sessionExpiry);
            command.ExecuteNonQuery();
        }

        using (var command = new NpgsqlCommand(
                   "UPDATE customer_otp_challenges SET consumed_at = @now WHERE challenge_id = @id", connection, transaction))
        {
            command.Parameters.AddWithValue("id", challengeId);
            command.Parameters.AddWithValue("now", now);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return CustomerAccountResult.Success(200, new
        {
            status = "VERIFIED",
            account_id = accountId,
            phone,
            phone_status = "VERIFIED",
            access_token = accessToken,
            expires_at = sessionExpiry,
            verified_at = verifiedAt
        });
    }

    public CustomerAccount? Authenticate(string accessToken, DateTimeOffset now)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand("""
            SELECT a.account_id, a.verified_phone, a.verified_at
            FROM customer_sessions s JOIN customer_accounts a ON a.account_id = s.account_id
            WHERE s.token_hash = @hash AND s.expires_at > @now LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("hash", CustomerAccountService.HashToken(accessToken));
        command.Parameters.AddWithValue("now", now);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new CustomerAccount(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2))
            : null;
    }

    public CustomerAccountResult ClaimInvoice(CustomerAccount account, string publicToken, DateTimeOffset now)
    {
        if (!PublicTokenGenerator.IsValid(publicToken))
        {
            return CustomerAccountResult.Failure(404, "PUBLIC_INVOICE_NOT_FOUND", "The requested invoice is not available.", "NONE");
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Guid storeId;
        Guid invoiceId;
        string? ownerPhone;
        using (var command = new NpgsqlCommand(
                   "SELECT store_id, invoice_id, customer_phone FROM public_invoices WHERE public_token = @token FOR UPDATE", connection, transaction))
        {
            command.Parameters.AddWithValue("token", publicToken);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return CustomerAccountResult.Failure(404, "PUBLIC_INVOICE_NOT_FOUND", "The requested invoice is not available.", "NONE");
            }

            storeId = reader.GetGuid(0);
            invoiceId = reader.GetGuid(1);
            ownerPhone = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        if (ownerPhone is not null && ownerPhone != account.VerifiedPhone)
        {
            return CustomerAccountResult.Failure(409, "INVOICE_CLAIM_CONFLICT", "The invoice belongs to a different verified phone.", "REVIEW");
        }

        using (var command = new NpgsqlCommand(
                   "SELECT account_id FROM customer_invoice_claims WHERE public_token = @token", connection, transaction))
        {
            command.Parameters.AddWithValue("token", publicToken);
            var claimedBy = command.ExecuteScalar();
            if (claimedBy is Guid accountId)
            {
                transaction.Commit();
                return accountId == account.AccountId
                    ? CustomerAccountResult.Success(200, new { status = "DUPLICATE", store_id = storeId, invoice_id = invoiceId })
                    : CustomerAccountResult.Failure(409, "INVOICE_CLAIM_CONFLICT", "The invoice was already claimed.", "REVIEW");
            }
        }

        using (var command = new NpgsqlCommand("""
            INSERT INTO customer_invoice_claims (account_id, store_id, invoice_id, public_token, claimed_at)
            VALUES (@account, @store, @invoice, @token, @claimed);
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("account", account.AccountId);
            command.Parameters.AddWithValue("store", storeId);
            command.Parameters.AddWithValue("invoice", invoiceId);
            command.Parameters.AddWithValue("token", publicToken);
            command.Parameters.AddWithValue("claimed", now);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return CustomerAccountResult.Success(201, new { status = "CLAIMED", store_id = storeId, invoice_id = invoiceId });
    }

    public IReadOnlyList<CustomerInvoiceListItem> ListInvoices(CustomerAccount account)
    {
        var items = new List<CustomerInvoiceListItem>();
        using var connection = Open();
        using var command = new NpgsqlCommand("""
            SELECT p.store_id, p.invoice_id, p.store_name, p.display_number, p.lifecycle_status, p.issued_at, p.public_token
            FROM public_invoices p
            LEFT JOIN customer_invoice_claims c ON c.public_token = p.public_token AND c.account_id = @account
            WHERE p.customer_phone = @phone OR c.account_id = @account
            ORDER BY p.store_id, p.invoice_id;
            """, connection);
        command.Parameters.AddWithValue("account", account.AccountId);
        command.Parameters.AddWithValue("phone", account.VerifiedPhone);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new CustomerInvoiceListItem(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt64(3),
                reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6)));
        }

        return items;
    }

    private NpgsqlConnection Open()
    {
        _cloudStore.EnsureInitialized();
        EnsureSchema();
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        if (_schemaReady)
        {
            return;
        }

        lock (_schemaSync)
        {
            if (_schemaReady)
            {
                return;
            }

            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            using var command = new NpgsqlCommand(SchemaSql, connection);
            command.ExecuteNonQuery();
            _schemaReady = true;
        }
    }
}
