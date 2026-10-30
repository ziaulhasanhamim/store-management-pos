using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using POSApp.Client.Security;
using POSApp.Persistence;
using TUnit.AspNetCore;

namespace POSApp.Tests.Integration.Infrastructure;

public class PosWebApplicationFactory : TestWebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        PostgreSqlTestDatabase.InitializeAsync().GetAwaiter().GetResult();
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_URL"] = PostgreSqlTestDatabase.ConnectionString,
                ["AdminUser:Enabled"] = "false",
                ["DevelopmentSeed:Enabled"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContextPool<AppDbContext>(o => o.UseNpgsql(PostgreSqlTestDatabase.ConnectionString));

            services.RemoveAll<IAntiforgery>();
            services.AddSingleton<IAntiforgery, TestAntiforgery>();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", AppRoles.Admin);
        client.DefaultRequestHeaders.Add("X-Test-Name", "Admin");
        return client;
    }

    public HttpClient CreateInventoryClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "2");
        client.DefaultRequestHeaders.Add("X-Test-Role", AppRoles.InventoryManager);
        client.DefaultRequestHeaders.Add("X-Test-Name", "InventoryManager");
        return client;
    }

    public HttpClient CreateSalesClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "3");
        client.DefaultRequestHeaders.Add("X-Test-Role", AppRoles.SalesAssociate);
        client.DefaultRequestHeaders.Add("X-Test-Name", "SalesAssociate");
        return client;
    }

    public HttpClient CreateAnonymousClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        return client;
    }

    public async Task ResetDatabaseAsync()
    {
        await PostgreSqlTestDatabase.ResetAsync();
    }
}
