using POSApp.Client.DTOs;
using POSApp.Client.Enums;

namespace POSApp.Client.Services;

public sealed class HttpPaymentClearingClient(HttpReceivingClient http) : IPaymentClearingClient
{
    public Task<PaymentClearingPageData> ReadAsync(int? locationId = null, PaymentMethod? method = null, CancellationToken ct = default)
    {
        var path = "api/finance/clearing";
        var queryParams = new List<string>();
        if (locationId.HasValue && locationId.Value > 0) queryParams.Add($"locationId={locationId.Value}");
        if (method.HasValue) queryParams.Add($"method={method.Value}");
        if (queryParams.Count > 0) path += "?" + string.Join("&", queryParams);

        return http.ReadAsync<PaymentClearingPageData>(path, ct);
    }

    public Task<IReadOnlyList<ClearingSaleItemDto>> GetUnsettledSalesAsync(int locationId, PaymentMethod method, CancellationToken ct = default)
    {
        return http.ReadAsync<IReadOnlyList<ClearingSaleItemDto>>($"api/finance/clearing/unsettled?locationId={locationId}&method={method}", ct);
    }

    public Task<int> RecordPayoutAsync(RecordPayoutInput input, CancellationToken ct = default)
    {
        return http.WriteAsync<RecordPayoutInput, int>("api/finance/clearing/payout", input, "api/finance/clearing/antiforgery");
    }
}
