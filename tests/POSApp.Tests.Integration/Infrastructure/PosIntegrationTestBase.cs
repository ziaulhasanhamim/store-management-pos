namespace POSApp.Tests.Integration.Infrastructure;

public abstract class PosIntegrationTestBase
{
    private static readonly PosWebApplicationFactory _factory = new();

    protected HttpClient AdminClient => _factory.CreateAdminClient();
    protected HttpClient SalesClient => _factory.CreateSalesClient();
    protected HttpClient InventoryClient => _factory.CreateInventoryClient();
    protected HttpClient AnonymousClient => _factory.CreateAnonymousClient();
    protected IServiceProvider Services => _factory.Services;

    [Before(Test)]
    public async Task SetupDatabaseAsync()
    {
        await PostgreSqlTestDatabase.ResetAsync();
    }
}
