using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public sealed class HttpSalesClient(HttpReceivingClient http) : ISalesClient
{
    public Task<IReadOnlyList<SalesProduct>> SearchAsync(int locationId, string? search, CancellationToken ct = default)
    {
        return http.ReadAsync<IReadOnlyList<SalesProduct>>($"api/sales/items?locationId={locationId}&search={Uri.EscapeDataString(search ?? "")}", ct);
    }

    public Task<SalesOptions> OptionsAsync()
    {
        return http.ReadAsync<SalesOptions>("api/sales/options");
    }

    public Task<SalesResult> SaveAsync(SalesCommand command, bool complete)
    {
        return http.WriteAsync<SalesCommand, SalesResult>(complete ? "api/sales/complete" : "api/sales/drafts", command, "api/sales/antiforgery");
    }

    public Task<SalesDraft?> DraftAsync(Guid id)
    {
        return http.ReadOptionalAsync<SalesDraft>("api/sales/drafts/" + id);
    }

    public Task<int> AddCustomerAsync(CustomerInput input)
    {
        return http.WriteAsync<CustomerInput, int>("api/sales/customers", input, "api/sales/antiforgery");
    }
}
