using Xunit;

namespace Kandoo.Cloud.Tests;

/// <summary>
/// Shares one real PostgreSQL process and serializes its consumers. In particular,
/// Windows must not start competing embedded servers on the same default port.
/// </summary>
[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresTestDatabase>
{
}
