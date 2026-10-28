using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.Security;


namespace POSApp.Tests.Integration.Infrastructure;

[NotInParallel("database")]
public abstract class PosIntegrationTestBase
{
    private static readonly PosWebApplicationFactory _factory = new();

    protected HttpClient AdminClient => _factory.CreateAdminClient();
    protected HttpClient SalesClient => _factory.CreateSalesClient();
    protected HttpClient InventoryClient => _factory.CreateInventoryClient();
    protected HttpClient AnonymousClient => _factory.CreateAnonymousClient();
    protected IServiceProvider Services => _factory.Services;

    protected ClaimsPrincipal AdminUser => CreatePrincipal(1, "Admin", AppRoles.Admin);
    protected ClaimsPrincipal InventoryUser => CreatePrincipal(2, "Inventory", AppRoles.InventoryManager);
    protected ClaimsPrincipal SalesUser => CreatePrincipal(3, "Sales", AppRoles.SalesAssociate);

    protected static ClaimsPrincipal CreatePrincipal(int userId, string name, string role) =>
        new(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role)
        ], "TestAuth"));

    protected AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    [Before(Test)]
    public async Task SetupDatabaseAsync()
    {
        await PostgreSqlTestDatabase.ResetAsync();
    }
}
