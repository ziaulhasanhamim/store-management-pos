using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public interface IMoneyEntryClient
{
    Task<MoneyEntryPage> ReadAsync(string kind, DateOnly? date = null, int page = 1, int pageSize = 30);
    Task<MoneyEntryResult> SaveAsync(string kind, MoneyEntryCommand input);
    Task<int> SaveCategoryAsync(ExpenseCategoryInput input);
}
