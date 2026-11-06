using Microsoft.AspNetCore.Authorization;

namespace POSApp.Security;

public static class AppPolicies
{
    public const string ManageStockAdjustment = "ManageStockAdjustment";
    public const string ManageUsers = "ManageUsers";
    public const string ManageInventory = "ManageInventory";
    public const string ViewCostPrice = "ViewCostPrice";
    public const string MakeSales = "MakeSales";
    public const string ManageDrawer = "ManageDrawer";
    public const string RecordExpense = "RecordExpense";
    public const string ManagePayroll = "ManagePayroll";
    public const string ManageParties = "ManageParties";
    public const string ApplyDiscount = "ApplyDiscount";
    public const string VoidSale = "VoidSale";
    public const string ViewFinancialReports = "ViewFinancialReports";

    public static IServiceCollection AddPosAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, DynamicPolicyAuthorizationHandler>();
        return services.AddAuthorization(options =>
        {
            options.AddPolicy(ManageStockAdjustment, policy => policy.Requirements.Add(new DynamicPolicyRequirement(ManageStockAdjustment)));
            options.AddPolicy(ManageUsers, policy => policy.RequireRole(AppRoles.Admin));
            options.AddPolicy(ManageInventory, policy => policy.Requirements.Add(new DynamicPolicyRequirement(ManageInventory)));
            options.AddPolicy(ViewCostPrice, policy => policy.Requirements.Add(new DynamicPolicyRequirement(ViewCostPrice)));
            options.AddPolicy(MakeSales, policy => policy.Requirements.Add(new DynamicPolicyRequirement(MakeSales)));
            options.AddPolicy(ManageDrawer, policy => policy.Requirements.Add(new DynamicPolicyRequirement(ManageDrawer)));
            options.AddPolicy(RecordExpense, policy => policy.Requirements.Add(new DynamicPolicyRequirement(RecordExpense)));
            options.AddPolicy(ManagePayroll, policy => policy.Requirements.Add(new DynamicPolicyRequirement(ManagePayroll)));
            options.AddPolicy(ManageParties, policy => policy.Requirements.Add(new DynamicPolicyRequirement(ManageParties)));
            options.AddPolicy(ApplyDiscount, policy => policy.Requirements.Add(new DynamicPolicyRequirement(ApplyDiscount)));
            options.AddPolicy(VoidSale, policy => policy.Requirements.Add(new DynamicPolicyRequirement(VoidSale)));
            options.AddPolicy(ViewFinancialReports, policy => policy.Requirements.Add(new DynamicPolicyRequirement(ViewFinancialReports)));
        });
    }
}
