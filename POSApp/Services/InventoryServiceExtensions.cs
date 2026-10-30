using POSApp.Client.Services;

namespace POSApp.Services;

public static class InventoryServiceExtensions
{
    public static IServiceCollection AddInventoryUi(this IServiceCollection services)
    {
        services.AddScoped<ImportActivity>();
        services.AddSingleton<IImportQueue, ImportQueue>();
        services.AddScoped<ImportService>();
        services.AddScoped<ImportProcessor>();
        services.AddScoped<IImportClient, ServerImportClient>();
        services.AddScoped<BalanceIntegrityService>();
        services.AddScoped<DraftService>();
        services.AddScoped<IDraftClient, ServerDraftClient>();
        services.AddScoped<StockAdjustmentService>();
        services.AddScoped<IStockAdjustmentClient, ServerStockAdjustmentClient>();
        services.AddScoped<StockTakeService>();
        services.AddScoped<IStockTakeClient, ServerStockTakeClient>();
        services.AddScoped<StockTransferService>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<ClosingService>();
        services.AddScoped<ClosureService>();
        services.AddScoped<BankService>();
        services.AddScoped<StatementService>();
        services.AddScoped<DebtService>();
        services.AddScoped<PartyAccountService>();
        services.AddScoped<CashDrawerService>();
        services.AddScoped<FinanceService>();
        services.AddScoped<MoneyEntryService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<RecordAdministration>();
        services.AddScoped<InventoryValuation>();
        services.AddScoped<BarcodeService>();
        services.AddScoped<SupplierService>();
        services.AddScoped<SalesService>();
        services.AddScoped<ReportService>();
        services.AddScoped<ReceivingReportService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<ISalesClient, ServerSalesClient>();
        services.AddScoped<IReceivingClient, ServerReceivingClient>();
        services.AddScoped<IAttributeDefinitionClient, ServerAttributeDefinitionClient>();
        services.AddScoped<IMoneyEntryClient, ServerMoneyEntryClient>();
        services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

        return services;
    }
}
