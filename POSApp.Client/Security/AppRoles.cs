namespace POSApp.Client.Security;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Cashier = "InventoryManager";
    public const string InventoryManager = "InventoryManager";
    public const string SalesAssociate = "SalesAssociate";
    public const string InventoryViewer = "InventoryViewer";
    public static IReadOnlyList<string> All { get; } = [Admin, InventoryManager, SalesAssociate, InventoryViewer];

    public static string DisplayName(string role) => role switch
    {
        Admin => "Admin",
        InventoryManager => "Cashier",
        SalesAssociate => "Sales Associate",
        InventoryViewer => "Inventory Viewer (Read-only Staff)",
        _ => role
    };
}
