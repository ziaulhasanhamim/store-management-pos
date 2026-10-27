namespace POSApp.Services;

public static class WorkspaceNavigation
{
    public static string Area(string path)
    {
        if (path.StartsWith("/reports"))
        {
            return "reports";
        }
        if (path.StartsWith("/administration") || path.StartsWith("/users"))
        {
            return "administration";
        }
        if (path.StartsWith("/finance") || path.StartsWith("/expenses") || path.StartsWith("/payroll")
            || path.StartsWith("/cash-drawer") || path.StartsWith("/accounting-date") || path.StartsWith("/accounts")
            || path.StartsWith("/employees") || path.StartsWith("/customers") || path.StartsWith("/suppliers/payments"))
        {
            return "finance";
        }
        if (path.StartsWith("/inventory") || path.StartsWith("/items") || path.StartsWith("/stock")
            || path.StartsWith("/receiving") || path.StartsWith("/suppliers"))
        {
            return "inventory";
        }
        return "";
    }

    public static string Tab(string path) => Area(path) switch
    {
        "finance" when path.StartsWith("/expenses") || path.StartsWith("/suppliers/payments")
            || path.StartsWith("/customers/payments") || path.StartsWith("/finance/debt/payments") => "/finance/payments",
        "finance" when path.StartsWith("/employees") || path.StartsWith("/payroll") || path.StartsWith("/customers") => "/finance/people",
        "finance" when path.StartsWith("/accounts") || path.StartsWith("/finance/accounts")
            || path.StartsWith("/finance/bank") || path.StartsWith("/finance/debt") => "/finance/accounts",
        "finance" => path == "/finance/payments" || path == "/finance/people" ? path : "/finance",
        "inventory" when path.StartsWith("/stock/takes") => "/stock/takes",
        "inventory" when path.StartsWith("/items") => "/items",
        "inventory" when path.StartsWith("/receiving") || path.StartsWith("/stock/transfers") => "/receiving",
        "inventory" when path.StartsWith("/suppliers") => "/suppliers",
        "inventory" => "/inventory",
        "administration" when path.StartsWith("/users") => "/users",
        "administration" when path.StartsWith("/administration/audit") => "/administration/audit",
        "administration" => "/administration",
        "reports" => path,
        _ => ""
    };
}
