using MysticMind.PostgresEmbed;

namespace Kandoo.Cloud.Tests;

/// <summary>
/// Uses the Linux CI service database when configured; otherwise starts a real
/// local PostgreSQL process so Windows runs the same tests without a container.
/// </summary>
public sealed class PostgresTestDatabase : IDisposable
{
    private readonly PgServer? _server;

    public PostgresTestDatabase()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__CloudPostgres");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            ConnectionString = configured;
            Store = new PostgresCloudStore(ConnectionString);
            return;
        }

        var workingDirectory = Path.Combine(Path.GetTempPath(), "kandoo-c05-" + Guid.NewGuid().ToString("N"));
        var server = new PgServer(
            "15.3.0",
            dbDir: workingDirectory,
            addLocalUserAccessPermission: true,
            clearInstanceDirOnStop: true);
        _server = server;
        server.Start();
        ConnectionString = $"Host=localhost;Port={server.PgPort};Database=postgres;Username=postgres;Password=test;Pooling=false";
        Store = new PostgresCloudStore(ConnectionString);
    }

    public string ConnectionString { get; }
    public PostgresCloudStore Store { get; }

    public void Dispose() => _server?.Dispose();
}
