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
}
