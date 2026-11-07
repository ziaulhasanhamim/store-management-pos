using POSApp.Client.DTOs;
using POSApp.Client.Enums;

namespace POSApp.Client.Services;

public interface IPaymentClearingClient
{
    Task<PaymentClearingPageData> ReadAsync(int? locationId = null, PaymentMethod? method = null, CancellationToken ct = default);
    Task<IReadOnlyList<ClearingSaleItemDto>> GetUnsettledSalesAsync(int locationId, PaymentMethod method, CancellationToken ct = default);
    Task<int> RecordPayoutAsync(RecordPayoutInput input, CancellationToken ct = default);
}
