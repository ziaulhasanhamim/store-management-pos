namespace POSApp.Client.Security;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string InventoryManager = "InventoryManager";
    public const string SalesAssociate = "SalesAssociate";
    public static IReadOnlyList<string> All { get; } = [Admin, InventoryManager, SalesAssociate];

    public static string DisplayName(string role) => role switch
    {
        Admin => "Admin",
        InventoryManager => "Inventory Manager / Store Manager",
        SalesAssociate => "Sales Associate",
        _ => role
    };
}
