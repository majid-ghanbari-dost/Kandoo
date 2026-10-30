using Npgsql;

namespace Kandoo.Cloud;

public sealed class PostgresSmsDeliveryStore : ISmsDeliveryStore
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS sms_store_counters (
            store_id uuid PRIMARY KEY,
            quota_period_start timestamptz NOT NULL,
            used_count integer NOT NULL,
            sent_count integer NOT NULL
        );
        CREATE TABLE IF NOT EXISTS sms_rate_buckets (
            store_id uuid NOT NULL,
            bucket_kind text NOT NULL,
            bucket_key text NOT NULL,
            window_start timestamptz NOT NULL,
            attempts integer NOT NULL,
            PRIMARY KEY (store_id, bucket_kind, bucket_key)
        );
        CREATE TABLE IF NOT EXISTS sms_deliveries (
            store_id uuid NOT NULL,
            operation_id text NOT NULL,
            invoice_id uuid NOT NULL,
            recipient_phone text NOT NULL,
            invoice_link text NOT NULL,
            template_name text NOT NULL,
            body text NOT NULL,
            request_fingerprint text NOT NULL,
            status text NOT NULL,
            provider_message_id text NULL,
            created_at timestamptz NOT NULL,
            sent_at timestamptz NULL,
            quota_remaining integer NOT NULL,
            rate_remaining integer NOT NULL,
            PRIMARY KEY (store_id, operation_id)
        );
        """;

    private const string DeliverySelect = """
        SELECT store_id, operation_id, invoice_id, recipient_phone, invoice_link,
               template_name, body, request_fingerprint, status, provider_message_id,
               created_at, sent_at, quota_remaining, rate_remaining
        FROM sms_deliveries
        """;

    private readonly string _connectionString;
    private readonly object _schemaSync = new();
    private bool _schemaReady;

    public PostgresSmsDeliveryStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public SmsBeginResult BeginSend(
        SmsSendRequest request,
        string body,
        string requestFingerprint,
        SmsSubscriptionMetadata subscription,
        SmsAbusePolicy policy,
        DateTimeOffset now)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var counter = LockCounter(connection, transaction, request.StoreId, subscription.QuotaPeriodStart);
        var existing = LoadForUpdate(connection, transaction, request.StoreId, request.OperationId);
        if (existing is not null)
        {
            SmsSendResult completed;
            if (!string.Equals(existing.Fingerprint, requestFingerprint, StringComparison.Ordinal))
            {
                completed = new SmsSendResult("OPERATION_ID_CONFLICT");
            }
            else if (existing.Status == "SENT")
            {
                completed = Result(existing, "DUPLICATE", true);
            }
            else
            {
                completed = new SmsSendResult("SMS_SEND_IN_PROGRESS", RetryAfterMs: 1000);
            }

            transaction.Commit();
            return new SmsBeginResult(completed);
        }

        if (counter.PeriodStart != subscription.QuotaPeriodStart)
        {
            counter = new CounterRow(subscription.QuotaPeriodStart, 0, 0);
            using var reset = new NpgsqlCommand("""
                UPDATE sms_store_counters SET quota_period_start = @period, used_count = 0, sent_count = 0
                WHERE store_id = @store
                """, connection, transaction);
            reset.Parameters.AddWithValue("store", request.StoreId);
            reset.Parameters.AddWithValue("period", subscription.QuotaPeriodStart);
            reset.ExecuteNonQuery();
        }

        if (counter.Used >= subscription.QuotaLimit)
        {
            transaction.Commit();
            return new SmsBeginResult(new SmsSendResult("SMS_QUOTA_EXHAUSTED"));
        }

        var windowStart = WindowStart(now);
        var invoiceBucket = LockBucket(connection, transaction, request.StoreId, "INVOICE", request.InvoiceId.ToString("D"), windowStart);
        var recipientBucket = LockBucket(connection, transaction, request.StoreId, "RECIPIENT", request.RecipientPhone, windowStart);
        if (invoiceBucket.Attempts >= policy.PerInvoicePerMinute || recipientBucket.Attempts >= policy.PerRecipientPerMinute)
        {
            var retry = Math.Max(1, (int)Math.Ceiling((windowStart.AddMinutes(1) - now).TotalMilliseconds));
            transaction.Commit();
            return new SmsBeginResult(new SmsSendResult("RATE_LIMITED", RetryAfterMs: retry));
        }

        var quotaRemaining = subscription.QuotaLimit - counter.Used - 1;
        var rateRemaining = Math.Min(
            policy.PerInvoicePerMinute - invoiceBucket.Attempts - 1,
            policy.PerRecipientPerMinute - recipientBucket.Attempts - 1);
        using (var insert = new NpgsqlCommand("""
            INSERT INTO sms_deliveries
                (store_id, operation_id, invoice_id, recipient_phone, invoice_link,
                 template_name, body, request_fingerprint, status, created_at,
                 quota_remaining, rate_remaining)
            VALUES (@store, @operation, @invoice, @phone, @link, @template, @body,
                    @fingerprint, 'PENDING', @now, @quota, @rate)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("store", request.StoreId);
            insert.Parameters.AddWithValue("operation", request.OperationId);
            insert.Parameters.AddWithValue("invoice", request.InvoiceId);
            insert.Parameters.AddWithValue("phone", request.RecipientPhone);
            insert.Parameters.AddWithValue("link", request.InvoiceLink);
            insert.Parameters.AddWithValue("template", SmsMessageTemplates.PersianInvoiceV1);
            insert.Parameters.AddWithValue("body", body);
            insert.Parameters.AddWithValue("fingerprint", requestFingerprint);
            insert.Parameters.AddWithValue("now", now);
            insert.Parameters.AddWithValue("quota", quotaRemaining);
            insert.Parameters.AddWithValue("rate", rateRemaining);
            insert.ExecuteNonQuery();
        }

        using (var updateCounter = new NpgsqlCommand(
                   "UPDATE sms_store_counters SET used_count = used_count + 1 WHERE store_id = @store",
                   connection,
                   transaction))
        {
            updateCounter.Parameters.AddWithValue("store", request.StoreId);
            updateCounter.ExecuteNonQuery();
        }

        IncrementBucket(connection, transaction, request.StoreId, "INVOICE", request.InvoiceId.ToString("D"), windowStart);
        IncrementBucket(connection, transaction, request.StoreId, "RECIPIENT", request.RecipientPhone, windowStart);
        transaction.Commit();
        return new SmsBeginResult(null);
    }

    public SmsSendResult CompleteSend(
        SmsSendRequest request,
        string body,
        string requestFingerprint,
        SmsProviderResult providerResult,
        DateTimeOffset now)
    {
        if (!string.Equals(body, providerResult.Body, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(providerResult.ProviderMessageId))
        {
            throw new InvalidOperationException("SMS_PROVIDER_RESULT_MISMATCH");
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var existing = LoadForUpdate(connection, transaction, request.StoreId, request.OperationId)
            ?? throw new InvalidOperationException("SMS_RESERVATION_NOT_FOUND");
        if (!string.Equals(existing.Fingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OPERATION_ID_CONFLICT");
        }

        if (existing.Status == "SENT")
        {
            transaction.Commit();
            return Result(existing, "DUPLICATE", true);
        }

        using (var update = new NpgsqlCommand("""
            UPDATE sms_deliveries SET status = 'SENT', provider_message_id = @provider,
                body = @body, sent_at = @now
            WHERE store_id = @store AND operation_id = @operation AND status = 'PENDING'
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("provider", providerResult.ProviderMessageId);
            update.Parameters.AddWithValue("body", body);
            update.Parameters.AddWithValue("now", now);
            update.Parameters.AddWithValue("store", request.StoreId);
            update.Parameters.AddWithValue("operation", request.OperationId);
            if (update.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException("SMS_RESERVATION_CONFLICT");
            }
        }

        using (var counter = new NpgsqlCommand(
                   "UPDATE sms_store_counters SET sent_count = sent_count + 1 WHERE store_id = @store",
                   connection,
                   transaction))
        {
            counter.Parameters.AddWithValue("store", request.StoreId);
            counter.ExecuteNonQuery();
        }

        var delivery = new SmsDelivery(
            request.StoreId, request.OperationId, request.InvoiceId, request.RecipientPhone,
            request.InvoiceLink, SmsMessageTemplates.PersianInvoiceV1, body,
            providerResult.ProviderMessageId, existing.CreatedAt, now,
            existing.QuotaRemaining, existing.RateRemaining);
        transaction.Commit();
        return new SmsSendResult("SENT", delivery);
    }

    public SmsStoreCounter? GetCounter(Guid storeId)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            "SELECT quota_period_start, used_count, sent_count FROM sms_store_counters WHERE store_id = @store",
            connection);
        command.Parameters.AddWithValue("store", storeId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new SmsStoreCounter(storeId, reader.GetFieldValue<DateTimeOffset>(0), reader.GetInt32(1), reader.GetInt32(2))
            : null;
    }

    public SmsDeliveryStatus? Get(Guid storeId, string operationId)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            DeliverySelect + " WHERE store_id = @store AND operation_id = @operation", connection);
        command.Parameters.AddWithValue("store", storeId);
        command.Parameters.AddWithValue("operation", operationId);
        using var reader = command.ExecuteReader();
        return !reader.Read()
            ? null
            : new SmsDeliveryStatus(
                reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetFieldValue<DateTimeOffset>(10),
                reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                reader.GetInt32(12), reader.GetInt32(13));
    }

    public SmsSendRequest? GetSource(Guid storeId, string operationId)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            DeliverySelect + " WHERE store_id = @store AND operation_id = @operation", connection);
        command.Parameters.AddWithValue("store", storeId);
        command.Parameters.AddWithValue("operation", operationId);
        using var reader = command.ExecuteReader();
        return !reader.Read()
            ? null
            : new SmsSendRequest(
                reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4));
    }

    private NpgsqlConnection Open()
    {
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

    private static CounterRow LockCounter(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid storeId,
        DateTimeOffset periodStart)
    {
        using var command = new NpgsqlCommand("""
            INSERT INTO sms_store_counters (store_id, quota_period_start, used_count, sent_count)
            VALUES (@store, @period, 0, 0)
            ON CONFLICT (store_id) DO UPDATE SET store_id = EXCLUDED.store_id
            RETURNING quota_period_start, used_count, sent_count
            """, connection, transaction);
        command.Parameters.AddWithValue("store", storeId);
        command.Parameters.AddWithValue("period", periodStart);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("SMS_COUNTER_UNAVAILABLE");
        }

        return new CounterRow(reader.GetFieldValue<DateTimeOffset>(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    private static BucketRow LockBucket(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid storeId,
        string kind,
        string key,
        DateTimeOffset windowStart)
    {
        DateTimeOffset currentWindow;
        int attempts;
        using (var command = new NpgsqlCommand("""
            INSERT INTO sms_rate_buckets (store_id, bucket_kind, bucket_key, window_start, attempts)
            VALUES (@store, @kind, @key, @window, 0)
            ON CONFLICT (store_id, bucket_kind, bucket_key) DO UPDATE SET store_id = EXCLUDED.store_id
            RETURNING window_start, attempts
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("store", storeId);
            command.Parameters.AddWithValue("kind", kind);
            command.Parameters.AddWithValue("key", key);
            command.Parameters.AddWithValue("window", windowStart);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("SMS_RATE_BUCKET_UNAVAILABLE");
            }

            currentWindow = reader.GetFieldValue<DateTimeOffset>(0);
            attempts = reader.GetInt32(1);
        }

        if (currentWindow != windowStart)
        {
            attempts = 0;
            using var reset = new NpgsqlCommand("""
                UPDATE sms_rate_buckets SET window_start = @window, attempts = 0
                WHERE store_id = @store AND bucket_kind = @kind AND bucket_key = @key
                """, connection, transaction);
            reset.Parameters.AddWithValue("store", storeId);
            reset.Parameters.AddWithValue("kind", kind);
            reset.Parameters.AddWithValue("key", key);
            reset.Parameters.AddWithValue("window", windowStart);
            reset.ExecuteNonQuery();
        }

        return new BucketRow(attempts);
    }

    private static void IncrementBucket(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid storeId,
        string kind,
        string key,
        DateTimeOffset windowStart)
    {
        using var command = new NpgsqlCommand("""
            UPDATE sms_rate_buckets SET attempts = attempts + 1
            WHERE store_id = @store AND bucket_kind = @kind AND bucket_key = @key AND window_start = @window
            """, connection, transaction);
        command.Parameters.AddWithValue("store", storeId);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("window", windowStart);
        command.ExecuteNonQuery();
    }

    private static DeliveryRow? LoadForUpdate(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid storeId,
        string operationId)
    {
        using var command = new NpgsqlCommand(
            DeliverySelect + " WHERE store_id = @store AND operation_id = @operation FOR UPDATE",
            connection,
            transaction);
        command.Parameters.AddWithValue("store", storeId);
        command.Parameters.AddWithValue("operation", operationId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new DeliveryRow(
            reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
            reader.GetInt32(12), reader.GetInt32(13));
    }

    private static SmsSendResult Result(DeliveryRow row, string status, bool replay)
    {
        var delivery = new SmsDelivery(
            row.StoreId, row.OperationId, row.InvoiceId, row.RecipientPhone, row.InvoiceLink,
            row.TemplateName, row.Body, row.ProviderMessageId ?? string.Empty,
            row.CreatedAt, row.SentAt ?? row.CreatedAt, row.QuotaRemaining, row.RateRemaining);
        return new SmsSendResult(status, delivery, replay);
    }

    private static DateTimeOffset WindowStart(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    }

    private sealed record CounterRow(DateTimeOffset PeriodStart, int Used, int Sent);
    private sealed record BucketRow(int Attempts);

    private sealed record DeliveryRow(
        Guid StoreId,
        string OperationId,
        Guid InvoiceId,
        string RecipientPhone,
        string InvoiceLink,
        string TemplateName,
        string Body,
        string Fingerprint,
        string Status,
        string? ProviderMessageId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? SentAt,
        int QuotaRemaining,
        int RateRemaining);
}
