namespace POSApp.Tests.Unit.Configuration;

public class ConnectionStringTests
{
    [Test]
    public async Task NormalizePostgresConnectionString_ConvertsUriToNpgsqlAdoFormat()
    {
        var uri = "postgres://posuser:secretpass@dpg-abc123-a.singapore-postgres.render.com:5432/posapp";
        var result = Program.NormalizePostgresConnectionString(uri);

        await result.Should().Contain("Host=dpg-abc123-a.singapore-postgres.render.com");
        await result.Should().Contain("Database=posapp");
        await result.Should().Contain("Username=posuser");
        await result.Should().Contain("Password=secretpass");
        await result.Should().Contain("Port=5432");
        await result.Should().Contain("SSL Mode=Require");
    }

    [Test]
    public async Task NormalizePostgresConnectionString_LeavesStandardAdoFormatUntouched()
    {
        var standard = "Host=localhost;Database=mydb;Username=postgres;Password=test;";
        var result = Program.NormalizePostgresConnectionString(standard);

        await result.Should().BeEqualTo(standard);
    }

    [Test]
    public async Task NormalizePostgresConnectionString_PreservesColonPasswordAndEscapedDatabase()
    {
        var result = new Npgsql.NpgsqlConnectionStringBuilder(
            Program.NormalizePostgresConnectionString("postgres://user:part:two%40three@localhost/store%20db"));
        await result.Password.Should().BeEqualTo("part:two@three");
        await result.Database.Should().BeEqualTo("store db");
    }

    [Test]
    public async Task NormalizePostgresConnectionString_ConfiguresSafePoolSizeAndIdleLifetime()
    {
        var result = new Npgsql.NpgsqlConnectionStringBuilder(
            Program.NormalizePostgresConnectionString("postgresql://user:pass@localhost:5432/testdb"));
        await result.MaxPoolSize.Should().BeEqualTo(8);
        await result.MinPoolSize.Should().BeEqualTo(0);
        await result.ConnectionIdleLifetime.Should().BeEqualTo(15);
    }

    [Test]
    public async Task NormalizePostgresConnectionString_AppliesQueryStringParameters()
    {
        var result = new Npgsql.NpgsqlConnectionStringBuilder(
            Program.NormalizePostgresConnectionString("postgresql://user:pass@localhost:5432/testdb?maxpoolsize=12&timeout=30"));
        await result.MaxPoolSize.Should().BeEqualTo(12);
        await result.Timeout.Should().BeEqualTo(30);
    }
}
