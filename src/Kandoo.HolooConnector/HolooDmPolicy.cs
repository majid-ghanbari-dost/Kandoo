using System.Data;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Kandoo.HolooConnector;

public enum HolooAccessTier
{
    Tier1WindowsAdminProvisioning = 1,
    Tier2OneTimeAdminPassword = 2,
    Tier3PrintCaptureFallback = 3,
}

public enum HolooCredentialSource
{
    WindowsIntegratedAuth = 1,
    OneTimeOperatorPrompt = 2,
    DedicatedReadOnlyLogin = 3,
    StoredFileOrRegistryForbidden = 4,
    GuessedDefaultForbidden = 5,
}

public sealed record HolooConsentInput(
    bool ConsentGranted,
    string ManagerName,
    string SqlInstanceName,
    string DatabaseName = "HolooCopy");

public sealed record HolooConsentRecord(
    Guid ConsentId,
    bool ConsentGranted,
    string ManagerName,
    string SqlInstanceName,
    string DatabaseName,
    HolooAccessTier Tier,
    DateTimeOffset GrantedAt,
    bool Revoked = false,
    DateTimeOffset? RevokedAt = null);

public sealed class HolooReadOnlyViolationException : InvalidOperationException
{
    public HolooReadOnlyViolationException(string message) : base(message) { }
}

public sealed class HolooCredentialPolicyException : InvalidOperationException
{
    public HolooCredentialPolicyException(string message) : base(message) { }
}

/// <summary>
/// Holds a one-time SQL admin password in memory only during Tier 2 provisioning and zeroes it on dispose.
/// Never serialized, persisted, or logged.
/// </summary>
public sealed class TransientAdminCredential : IDisposable
{
    private char[] _passwordChars;
    private bool _disposed;

    public string UserId { get; }
    public HolooCredentialSource Source { get; }
    public bool IsCleared => _disposed;

    public TransientAdminCredential(
        string userId,
        ReadOnlySpan<char> password,
        HolooCredentialSource source = HolooCredentialSource.OneTimeOperatorPrompt)
    {
        if (source is HolooCredentialSource.StoredFileOrRegistryForbidden or HolooCredentialSource.GuessedDefaultForbidden)
        {
            throw new HolooCredentialPolicyException(
                $"Credential source '{source}' violates D-M policy: Holoo connector never reads stored passwords or guesses defaults.");
        }

        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("SQL admin user ID is required.", nameof(userId));
        if (password.IsEmpty)
            throw new ArgumentException("One-time SQL admin password cannot be empty.", nameof(password));

        UserId = userId.Trim();
        Source = source;
        _passwordChars = password.ToArray();
    }

    internal string ExposeOnceForConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new string(_passwordChars);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Array.Clear(_passwordChars, 0, _passwordChars.Length);
        _disposed = true;
    }

    public override string ToString() => $"TransientAdminCredential(UserId={UserId}, Cleared={_disposed})";
}

/// <summary>
/// Rejects any non-read-only SQL before execution. Fail-closed gate across both live SQL Server and synthetic SQLite.
/// </summary>
public static partial class HolooQueryGuard
{
    private static readonly Regex ForbiddenKeywords = BuildForbiddenRegex();

    public static void EnsureReadOnlySelect(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new HolooReadOnlyViolationException("SQL command cannot be empty.");

        var normalized = StripCommentsAndStrings(sql).Trim();
        if (!(normalized.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
              normalized.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)))
        {
            throw new HolooReadOnlyViolationException(
                "Only SELECT/WITH read-only queries are permitted against Holoo.");
        }

        var trimmedEnd = normalized.TrimEnd();
        if (trimmedEnd.EndsWith(';'))
            trimmedEnd = trimmedEnd[..^1].TrimEnd();

        if (trimmedEnd.Contains(';', StringComparison.Ordinal))
            throw new HolooReadOnlyViolationException("Multi-statement SQL batches are forbidden in read-only mode.");

        if (ForbiddenKeywords.IsMatch(trimmedEnd))
            throw new HolooReadOnlyViolationException("Write/DDL/administrative SQL keyword detected and blocked.");
    }

    private static string StripCommentsAndStrings(string sql)
    {
        var buffer = new char[sql.Length];
        var len = 0;
        var i = 0;
        while (i < sql.Length)
        {
            if (sql[i] == '\'' || sql[i] == '"')
            {
                var quote = sql[i];
                buffer[len++] = ' ';
                i++;
                var closed = false;
                while (i < sql.Length)
                {
                    if (sql[i] == quote && i + 1 < sql.Length && sql[i + 1] == quote)
                    {
                        i += 2;
                        continue;
                    }

                    if (sql[i] == quote)
                    {
                        i++;
                        closed = true;
                        break;
                    }

                    i++;
                }

                if (!closed)
                    throw new HolooReadOnlyViolationException("Unterminated SQL string or quoted identifier detected.");

                continue;
            }

            if (sql[i] == '[')
            {
                buffer[len++] = ' ';
                i++;
                var closed = false;
                while (i < sql.Length)
                {
                    if (sql[i] == ']' && i + 1 < sql.Length && sql[i + 1] == ']')
                    {
                        i += 2;
                        continue;
                    }

                    if (sql[i] == ']')
                    {
                        i++;
                        closed = true;
                        break;
                    }

                    i++;
                }

                if (!closed)
                    throw new HolooReadOnlyViolationException("Unterminated bracketed SQL identifier detected.");

                continue;
            }

            if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                    i++;
                buffer[len++] = ' ';
                continue;
            }

            if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                var closed = false;
                while (i + 1 < sql.Length)
                {
                    if (sql[i] == '*' && sql[i + 1] == '/')
                    {
                        i += 2;
                        closed = true;
                        break;
                    }

                    i++;
                }

                if (!closed)
                    throw new HolooReadOnlyViolationException("Unterminated block SQL comment detected.");

                buffer[len++] = ' ';
                continue;
            }

            buffer[len++] = sql[i++];
        }

        return new string(buffer, 0, len);
    }

    [GeneratedRegex(
        @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|GRANT|REVOKE|DENY|BACKUP|RESTORE|INTO)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BuildForbiddenRegex();
}

/// <summary>
/// Builds safe, identifier-validated T-SQL scripts to create and cleanly uninstall
/// a least-privilege read-only SQL login on the owner's local HolooCopy database.
/// </summary>
public static partial class HolooReadOnlyProvisioningScriptBuilder
{
    public const string DefaultReadOnlyLoginName = "kandoo_holoo_ro";
    private static readonly Regex SafeIdentifierRegex = BuildSafeIdentifierRegex();
    private static readonly Regex SafeDatabaseRegex = BuildSafeDatabaseRegex();

    public static string ValidateIdentifier(string identifier, string paramName)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("SQL identifier cannot be empty.", paramName);

        var trimmed = identifier.Trim();
        if (!SafeIdentifierRegex.IsMatch(trimmed))
            throw new ArgumentException($"Unsafe SQL identifier '{trimmed}'. Only ASCII letters, digits, and '_' are allowed.", paramName);

        return trimmed;
    }

    public static string ValidateDatabaseName(string databaseName, string paramName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("SQL database name cannot be empty.", paramName);

        var trimmed = databaseName.Trim();
        if (!SafeDatabaseRegex.IsMatch(trimmed))
            throw new ArgumentException($"Unsafe SQL database name '{trimmed}'. Only ASCII letters, digits, '_', and '-' are allowed.", paramName);

        return trimmed;
    }

    public static string BracketEscape(string identifier, string paramName)
    {
        var safe = ValidateIdentifier(identifier, paramName);
        return $"[{safe.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    public static string BracketEscapeDatabase(string databaseName, string paramName)
    {
        var safe = ValidateDatabaseName(databaseName, paramName);
        return $"[{safe.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    public static string BuildCheckAdminSql() =>
        "SELECT CAST(CASE WHEN IS_SRVROLEMEMBER('sysadmin') = 1 OR IS_SRVROLEMEMBER('securityadmin') = 1 THEN 1 ELSE 0 END AS INT);";

    public static string BuildCreateReadOnlyLoginSql(
        string databaseName,
        string loginName = DefaultReadOnlyLoginName,
        string? initialPassword = null)
    {
        var dbBracketed = BracketEscapeDatabase(databaseName, nameof(databaseName));
        var loginSafe = ValidateIdentifier(loginName, nameof(loginName));
        var loginBracketed = BracketEscape(loginSafe, nameof(loginName));
        var secret = string.IsNullOrEmpty(initialPassword) ? GenerateEphemeralStrongPassword() : initialPassword;
        var secretLiteral = EscapeSqlStringLiteral(secret);

        return $"""
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{loginSafe}')
            BEGIN
                CREATE LOGIN {loginBracketed} WITH PASSWORD = N'{secretLiteral}', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
            END;
            USE {dbBracketed};
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{loginSafe}')
            BEGIN
                CREATE USER {loginBracketed} FOR LOGIN {loginBracketed};
            END;
            ALTER ROLE [db_datareader] ADD MEMBER {loginBracketed};
            DENY INSERT, UPDATE, DELETE, ALTER, CONTROL ON DATABASE::{dbBracketed} TO {loginBracketed};
            """;
    }

    public static string BuildUninstallReadOnlyLoginSql(
        string databaseName,
        string loginName = DefaultReadOnlyLoginName)
    {
        var dbSafe = ValidateDatabaseName(databaseName, nameof(databaseName));
        var dbBracketed = BracketEscapeDatabase(dbSafe, nameof(databaseName));
        var loginSafe = ValidateIdentifier(loginName, nameof(loginName));
        var loginBracketed = BracketEscape(loginSafe, nameof(loginName));

        return $"""
            IF DB_ID(N'{EscapeSqlStringLiteral(dbSafe)}') IS NOT NULL
            BEGIN
                USE {dbBracketed};
                IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{loginSafe}')
                    DROP USER {loginBracketed};
            END;
            IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{loginSafe}')
            BEGIN
                DROP LOGIN {loginBracketed};
            END;
            """;
    }

    public static string GenerateEphemeralStrongPassword()
    {
        Span<byte> random = stackalloc byte[24];
        RandomNumberGenerator.Fill(random);
        return "Kd!" + Convert.ToBase64String(random) + "9z";
    }

    private static string EscapeSqlStringLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildSafeIdentifierRegex();

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildSafeDatabaseRegex();
}

/// <summary>
/// Builds SQL Server connection strings strictly pinned to read-only intent and never PersistSecurityInfo=true.
/// </summary>
public static class HolooSqlConnectionStringFactory
{
    public static string BuildReadOnlyIntegrated(
        string instanceName,
        string databaseName,
        int connectTimeoutSeconds = 5)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
            throw new ArgumentException("SQL Server instance name is required.", nameof(instanceName));
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name is required.", nameof(databaseName));

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = instanceName.Trim(),
            InitialCatalog = databaseName.Trim(),
            IntegratedSecurity = true,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            TrustServerCertificate = true,
            PersistSecurityInfo = false,
            ConnectTimeout = Math.Clamp(connectTimeoutSeconds, 1, 60),
        };
        return builder.ConnectionString;
    }

    public static string BuildReadOnlySqlAuth(
        string instanceName,
        string databaseName,
        string readOnlyUserId,
        string readOnlyPassword,
        int connectTimeoutSeconds = 5)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
            throw new ArgumentException("SQL Server instance name is required.", nameof(instanceName));
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name is required.", nameof(databaseName));
        if (string.IsNullOrWhiteSpace(readOnlyUserId))
            throw new ArgumentException("Read-only SQL user ID is required.", nameof(readOnlyUserId));
        if (string.IsNullOrEmpty(readOnlyPassword))
            throw new ArgumentException("Read-only SQL password is required.", nameof(readOnlyPassword));

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = instanceName.Trim(),
            InitialCatalog = databaseName.Trim(),
            IntegratedSecurity = false,
            UserID = readOnlyUserId.Trim(),
            Password = readOnlyPassword,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            TrustServerCertificate = true,
            PersistSecurityInfo = false,
            ConnectTimeout = Math.Clamp(connectTimeoutSeconds, 1, 60),
        };
        return builder.ConnectionString;
    }

    internal static string BuildAdminProvisioningIntegrated(
        string instanceName,
        string databaseName,
        int connectTimeoutSeconds = 5)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = instanceName.Trim(),
            InitialCatalog = databaseName.Trim(),
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            PersistSecurityInfo = false,
            ConnectTimeout = Math.Clamp(connectTimeoutSeconds, 1, 60),
        };
        return builder.ConnectionString;
    }

    internal static string BuildAdminProvisioningSqlAuth(
        string instanceName,
        string databaseName,
        TransientAdminCredential credential,
        int connectTimeoutSeconds = 5)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = instanceName.Trim(),
            InitialCatalog = databaseName.Trim(),
            IntegratedSecurity = false,
            UserID = credential.UserId,
            Password = credential.ExposeOnceForConnection(),
            TrustServerCertificate = true,
            PersistSecurityInfo = false,
            ConnectTimeout = Math.Clamp(connectTimeoutSeconds, 1, 60),
        };
        return builder.ConnectionString;
    }

    public static string Redact(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.Password))
            builder.Password = "***";
        return builder.ConnectionString;
    }
}

public sealed record HolooProvisioningOutcome(
    bool Succeeded,
    string? ReadOnlyLoginName,
    string? ReadOnlyPassword,
    string? FailureReason);

public interface IHolooProvisioningTransport
{
    Task<bool> HasWindowsSqlAdminAsync(string instanceName, string databaseName, CancellationToken cancellationToken = default);
    Task<HolooProvisioningOutcome> ProvisionReadOnlyViaWindowsAdminAsync(
        string instanceName,
        string databaseName,
        string loginName,
        CancellationToken cancellationToken = default);
    Task<HolooProvisioningOutcome> ProvisionReadOnlyViaOneTimeAdminAsync(
        string instanceName,
        string databaseName,
        string loginName,
        TransientAdminCredential adminCredential,
        CancellationToken cancellationToken = default);
    Task<bool> VerifyReadOnlySelectAsync(string readOnlyConnectionString, CancellationToken cancellationToken = default);
    Task<bool> UninstallReadOnlyLoginAsync(
        string instanceName,
        string databaseName,
        string loginName,
        TransientAdminCredential? adminCredential = null,
        CancellationToken cancellationToken = default);
}

public sealed class SqlServerHolooProvisioningTransport : IHolooProvisioningTransport
{
    private readonly int _connectTimeoutSeconds;

    public SqlServerHolooProvisioningTransport(int connectTimeoutSeconds = 5)
    {
        _connectTimeoutSeconds = Math.Clamp(connectTimeoutSeconds, 1, 60);
    }

    public async Task<bool> HasWindowsSqlAdminAsync(
        string instanceName,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connStr = HolooSqlConnectionStringFactory.BuildAdminProvisioningIntegrated(
                instanceName,
                databaseName,
                _connectTimeoutSeconds);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = HolooReadOnlyProvisioningScriptBuilder.BuildCheckAdminSql();
            cmd.CommandType = CommandType.Text;
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result) == 1;
        }
        catch
        {
            return false;
        }
    }

    public async Task<HolooProvisioningOutcome> ProvisionReadOnlyViaWindowsAdminAsync(
        string instanceName,
        string databaseName,
        string loginName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var password = HolooReadOnlyProvisioningScriptBuilder.GenerateEphemeralStrongPassword();
            var sql = HolooReadOnlyProvisioningScriptBuilder.BuildCreateReadOnlyLoginSql(databaseName, loginName, password);
            var connStr = HolooSqlConnectionStringFactory.BuildAdminProvisioningIntegrated(
                instanceName,
                databaseName,
                _connectTimeoutSeconds);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new HolooProvisioningOutcome(true, loginName, password, null);
        }
        catch (Exception ex)
        {
            return new HolooProvisioningOutcome(false, null, null, $"TIER1_PROVISION_FAILED: {ex.GetType().Name}");
        }
    }

    public async Task<HolooProvisioningOutcome> ProvisionReadOnlyViaOneTimeAdminAsync(
        string instanceName,
        string databaseName,
        string loginName,
        TransientAdminCredential adminCredential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adminCredential);
        try
        {
            var password = HolooReadOnlyProvisioningScriptBuilder.GenerateEphemeralStrongPassword();
            var sql = HolooReadOnlyProvisioningScriptBuilder.BuildCreateReadOnlyLoginSql(databaseName, loginName, password);
            var connStr = HolooSqlConnectionStringFactory.BuildAdminProvisioningSqlAuth(
                instanceName,
                databaseName,
                adminCredential,
                _connectTimeoutSeconds);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new HolooProvisioningOutcome(true, loginName, password, null);
        }
        catch (Exception ex)
        {
            return new HolooProvisioningOutcome(false, null, null, $"TIER2_PROVISION_FAILED: {ex.GetType().Name}");
        }
        finally
        {
            adminCredential.Dispose();
        }
    }

    public async Task<bool> VerifyReadOnlySelectAsync(
        string readOnlyConnectionString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new SqlConnection(readOnlyConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(value) == 1;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UninstallReadOnlyLoginAsync(
        string instanceName,
        string databaseName,
        string loginName,
        TransientAdminCredential? adminCredential = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = HolooReadOnlyProvisioningScriptBuilder.BuildUninstallReadOnlyLoginSql(databaseName, loginName);
            var connStr = adminCredential is null
                ? HolooSqlConnectionStringFactory.BuildAdminProvisioningIntegrated(instanceName, databaseName, _connectTimeoutSeconds)
                : HolooSqlConnectionStringFactory.BuildAdminProvisioningSqlAuth(instanceName, databaseName, adminCredential, _connectTimeoutSeconds);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            adminCredential?.Dispose();
        }
    }
}

public sealed record HolooConnectionPlan(
    HolooAccessTier SelectedTier,
    bool SqlConnected,
    bool UsePrintCaptureFallback,
    string? ReadOnlyConnectionString,
    string? ProvisionedLoginName,
    HolooConsentRecord? Consent,
    string? FallbackReason,
    string? RedactedConnectionDescription);

/// <summary>
/// Implements the D-M three-tier access cascade on top of owner consent and zero stored secrets:
/// Tier 1 (Windows admin creates dedicated read-only login) ->
/// Tier 2 (one-time admin password creates dedicated read-only login and clears password) ->
/// Tier 3 (automatic fallback to print capture).
/// </summary>
public sealed class HolooConnectionCoordinator
{
    private readonly IHolooProvisioningTransport _transport;
    private readonly SqliteHolooShadowStore? _consentStore;

    public HolooConnectionCoordinator(
        IHolooProvisioningTransport? transport = null,
        SqliteHolooShadowStore? consentStore = null)
    {
        _transport = transport ?? new SqlServerHolooProvisioningTransport();
        _consentStore = consentStore;
    }

    public async Task<HolooConnectionPlan> EstablishAsync(
        HolooConsentInput consentInput,
        Func<TransientAdminCredential?>? oneTimeAdminPrompt = null,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consentInput);
        var timestamp = now ?? DateTimeOffset.UtcNow;

        if (!consentInput.ConsentGranted || string.IsNullOrWhiteSpace(consentInput.ManagerName))
        {
            return Fallback(
                null,
                "CONSENT_REQUIRED: Owner consent tick and manager name are required before connecting to Holoo.");
        }

        if (string.IsNullOrWhiteSpace(consentInput.SqlInstanceName) ||
            string.IsNullOrWhiteSpace(consentInput.DatabaseName))
        {
            return Fallback(null, "HOLOO_INSTANCE_UNSPECIFIED");
        }

        // Tier 1: Windows integrated admin provisioning on local HolooCopy
        if (await _transport.HasWindowsSqlAdminAsync(consentInput.SqlInstanceName, consentInput.DatabaseName, cancellationToken).ConfigureAwait(false))
        {
            var tier1 = await _transport.ProvisionReadOnlyViaWindowsAdminAsync(
                consentInput.SqlInstanceName,
                consentInput.DatabaseName,
                HolooReadOnlyProvisioningScriptBuilder.DefaultReadOnlyLoginName,
                cancellationToken).ConfigureAwait(false);

            if (tier1.Succeeded && tier1.ReadOnlyLoginName is not null && tier1.ReadOnlyPassword is not null)
            {
                var roConn = HolooSqlConnectionStringFactory.BuildReadOnlySqlAuth(
                    consentInput.SqlInstanceName,
                    consentInput.DatabaseName,
                    tier1.ReadOnlyLoginName,
                    tier1.ReadOnlyPassword);

                if (await _transport.VerifyReadOnlySelectAsync(roConn, cancellationToken).ConfigureAwait(false))
                {
                    var consent = RecordConsent(consentInput, HolooAccessTier.Tier1WindowsAdminProvisioning, timestamp);
                    return new HolooConnectionPlan(
                        HolooAccessTier.Tier1WindowsAdminProvisioning,
                        SqlConnected: true,
                        UsePrintCaptureFallback: false,
                        ReadOnlyConnectionString: roConn,
                        ProvisionedLoginName: tier1.ReadOnlyLoginName,
                        Consent: consent,
                        FallbackReason: null,
                        RedactedConnectionDescription: HolooSqlConnectionStringFactory.Redact(roConn));
                }
            }
        }

        // Tier 2: One-time SQL admin password prompt (never persisted or logged)
        TransientAdminCredential? oneTimeCredential = null;
        try
        {
            oneTimeCredential = oneTimeAdminPrompt?.Invoke();
            if (oneTimeCredential is not null)
            {
                var tier2 = await _transport.ProvisionReadOnlyViaOneTimeAdminAsync(
                    consentInput.SqlInstanceName,
                    consentInput.DatabaseName,
                    HolooReadOnlyProvisioningScriptBuilder.DefaultReadOnlyLoginName,
                    oneTimeCredential,
                    cancellationToken).ConfigureAwait(false);

                if (tier2.Succeeded && tier2.ReadOnlyLoginName is not null && tier2.ReadOnlyPassword is not null)
                {
                    var roConn = HolooSqlConnectionStringFactory.BuildReadOnlySqlAuth(
                        consentInput.SqlInstanceName,
                        consentInput.DatabaseName,
                        tier2.ReadOnlyLoginName,
                        tier2.ReadOnlyPassword);

                    if (await _transport.VerifyReadOnlySelectAsync(roConn, cancellationToken).ConfigureAwait(false))
                    {
                        var consent = RecordConsent(consentInput, HolooAccessTier.Tier2OneTimeAdminPassword, timestamp);
                        return new HolooConnectionPlan(
                            HolooAccessTier.Tier2OneTimeAdminPassword,
                            SqlConnected: true,
                            UsePrintCaptureFallback: false,
                            ReadOnlyConnectionString: roConn,
                            ProvisionedLoginName: tier2.ReadOnlyLoginName,
                            Consent: consent,
                            FallbackReason: null,
                            RedactedConnectionDescription: HolooSqlConnectionStringFactory.Redact(roConn));
                    }
                }
            }
        }
        catch (HolooCredentialPolicyException)
        {
            throw;
        }
        finally
        {
            oneTimeCredential?.Dispose();
        }

        // Tier 3: Automatic fallback to print capture
        var tier3Consent = RecordConsent(consentInput, HolooAccessTier.Tier3PrintCaptureFallback, timestamp);
        return Fallback(
            tier3Consent,
            "TIER3_PRINT_CAPTURE_FALLBACK: SQL Server admin provisioning unavailable; falling back to print capture.");
    }

    public async Task<bool> UninstallAsync(
        HolooConsentRecord consent,
        TransientAdminCredential? oneTimeAdminCredential = null,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consent);
        try
        {
            var removed = await _transport.UninstallReadOnlyLoginAsync(
                consent.SqlInstanceName,
                consent.DatabaseName,
                HolooReadOnlyProvisioningScriptBuilder.DefaultReadOnlyLoginName,
                oneTimeAdminCredential,
                cancellationToken).ConfigureAwait(false);

            if (removed && _consentStore is not null)
                _consentStore.RevokeConsent(consent.ConsentId, now ?? DateTimeOffset.UtcNow);

            return removed;
        }
        finally
        {
            oneTimeAdminCredential?.Dispose();
        }
    }

    private HolooConsentRecord RecordConsent(
        HolooConsentInput input,
        HolooAccessTier tier,
        DateTimeOffset timestamp)
    {
        var record = new HolooConsentRecord(
            Guid.NewGuid(),
            input.ConsentGranted,
            input.ManagerName.Trim(),
            input.SqlInstanceName.Trim(),
            input.DatabaseName.Trim(),
            tier,
            timestamp);

        _consentStore?.RecordConsent(record);
        return record;
    }

    private static HolooConnectionPlan Fallback(HolooConsentRecord? consent, string reason) =>
        new(
            HolooAccessTier.Tier3PrintCaptureFallback,
            SqlConnected: false,
            UsePrintCaptureFallback: true,
            ReadOnlyConnectionString: null,
            ProvisionedLoginName: null,
            Consent: consent,
            FallbackReason: reason,
            RedactedConnectionDescription: null);
}
