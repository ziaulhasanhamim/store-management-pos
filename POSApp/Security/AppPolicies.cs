namespace POSApp.Security;

public static class AppPolicies
{
    public const string ManageStockAdjustment = "ManageStockAdjustment";
    public const string ManageUsers = "ManageUsers";
    public const string ManageInventory = "ManageInventory";
    public const string ViewCostPrice = "ViewCostPrice";
    public const string MakeSales = "MakeSales";

    public static IServiceCollection AddPosAuthorization(this IServiceCollection services) =>
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ManageStockAdjustment, policy => policy.RequireRole(AppRoles.Admin));
            options.AddPolicy(ManageUsers, policy => policy.RequireRole(AppRoles.Admin));
            options.AddPolicy(ManageInventory, policy => policy.RequireRole(AppRoles.Admin, AppRoles.InventoryManager));
            options.AddPolicy(ViewCostPrice, policy => policy.RequireRole(AppRoles.Admin, AppRoles.InventoryManager));
            options.AddPolicy(MakeSales, policy => policy.RequireRole(AppRoles.Admin, AppRoles.InventoryManager, AppRoles.SalesAssociate));
        });
}
