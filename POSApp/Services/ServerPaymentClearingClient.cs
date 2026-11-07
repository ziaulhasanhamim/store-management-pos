using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Client.Services;

namespace POSApp.Services;

public sealed class ServerPaymentClearingClient : IPaymentClearingClient, IDisposable
{
    private readonly PaymentClearingService clearing;
    private readonly AuthenticationStateProvider authentication;
    private Task<AuthenticationState>? state;

    public ServerPaymentClearingClient(PaymentClearingService clearing, AuthenticationStateProvider authentication)
    {
        this.clearing = clearing;
        this.authentication = authentication;
        authentication.AuthenticationStateChanged += StateChanged;
    }

    private void StateChanged(Task<AuthenticationState> current)
    {
        state = current;
    }

    private Task<AuthenticationState> StateAsync() => state ?? authentication.GetAuthenticationStateAsync();

    public async Task<PaymentClearingPageData> ReadAsync(int? locationId = null, PaymentMethod? method = null, CancellationToken ct = default)
    {
        var user = (await StateAsync()).User;
        return await clearing.GetPageDataAsync(user, locationId, method, ct);
    }

    public async Task<IReadOnlyList<ClearingSaleItemDto>> GetUnsettledSalesAsync(int locationId, PaymentMethod method, CancellationToken ct = default)
    {
        var user = (await StateAsync()).User;
        return await clearing.GetUnsettledSalesAsync(user, locationId, method, ct);
    }

    public async Task<int> RecordPayoutAsync(RecordPayoutInput input, CancellationToken ct = default)
    {
        var user = (await StateAsync()).User;
        return await clearing.RecordPayoutAsync(user, input, ct);
    }

    public void Dispose()
    {
        authentication.AuthenticationStateChanged -= StateChanged;
    }
}
