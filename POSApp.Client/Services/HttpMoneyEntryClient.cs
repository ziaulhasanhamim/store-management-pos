using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public sealed class HttpMoneyEntryClient(HttpReceivingClient http) : IMoneyEntryClient
{
    public Task<MoneyEntryPage> ReadAsync(string kind, DateOnly? date = null, int page = 1, int pageSize = 30)
    {
        var path = $"api/entries/{kind}?page={page}&pageSize={pageSize}";
        if (date is not null)
        {
            path += $"&date={date:yyyy-MM-dd}";
        }
        return http.ReadAsync<MoneyEntryPage>(path);
    }

    public Task<MoneyEntryResult> SaveAsync(string kind, MoneyEntryCommand input)
    {
        return http.WriteAsync<MoneyEntryCommand, MoneyEntryResult>($"api/entries/{kind}", input, "api/entries/antiforgery");
    }

    public Task<int> SaveCategoryAsync(ExpenseCategoryInput input)
    {
        return http.WriteAsync<ExpenseCategoryInput, int>("api/entries/categories", input, "api/entries/antiforgery");
    }
}
