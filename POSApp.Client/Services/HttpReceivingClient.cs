using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public sealed class HttpReceivingClient(HttpClient http) : IReceivingClient
{
    private string? _cachedToken;
    public Task<IReadOnlyList<ItemRow>> SearchAsync(string search, CancellationToken cancellationToken = default, int? locationId = null)
    {
        var path = $"api/inventory/items?search={Uri.EscapeDataString(search)}";
        if (locationId is not null)
        {
            path += $"&locationId={locationId}";
        }
        return ReadAsync<IReadOnlyList<ItemRow>>(path, cancellationToken);
    }

    public Task<TransferResult> TransferAsync(TransferCommand command)
    {
        return WriteAsync<TransferCommand, TransferResult>("api/inventory/transfers", command);
    }

    public Task<ReceivingOptions> OptionsAsync()
    {
        return ReadAsync<ReceivingOptions>("api/inventory/options");
    }

    public Task<ItemRow> SaveProductAsync(ProductInput input)
    {
        return WriteAsync<ProductInput, ItemRow>("api/inventory/items", input);
    }

    public Task<int> AddSupplierAsync(SupplierInput input)
    {
        return WriteAsync<SupplierInput, int>("api/inventory/suppliers", input);
    }

    public Task<int> AddLocationAsync(LocationInput input)
    {
        return WriteAsync<LocationInput, int>("api/inventory/locations", input);
    }

    public Task<ReceiveResult> ReceiveAsync(ReceiveCommand command)
    {
        return WriteAsync<ReceiveCommand, ReceiveResult>("api/inventory/receipts", command);
    }

    public async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    public async Task<T?> ReadOptionalAsync<T>(string path, CancellationToken cancellationToken = default) where T : class
    {
        using var response = await http.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }
        return await ReadResponseAsync<T?>(response, cancellationToken, allowEmpty: true);
    }

    public async Task<TResult> WriteAsync<TInput, TResult>(string path, TInput input, string tokenPath = "api/inventory/antiforgery")
    {
        // The same-origin browser sends the Identity and antiforgery cookies automatically.
        var token = _cachedToken;
        if (string.IsNullOrEmpty(token))
        {
            var responseToken = await ReadAsync<AntiforgeryResponse>(tokenPath);
            token = _cachedToken = responseToken.Token;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = JsonContent.Create(input);
        request.Headers.Add("X-CSRF-TOKEN", token);

        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _cachedToken = null;
        }
        return await ReadResponseAsync<TResult>(response);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken = default, bool allowEmpty = false)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Your session expired. Sign in again.",
                HttpStatusCode.Forbidden => "Your account no longer has access to this operation.",
                _ => "The request failed. Please try again."
            };

            try
            {
                var error = await response.Content.ReadFromJsonAsync<InventoryApiError>(cancellationToken);
                message = error?.Message ?? message;
            }
            catch (JsonException)
            {
                // Keep the status-based message if a proxy returned a non-JSON response.
            }

            throw new InventoryRequestException(message);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        if (result is null && !allowEmpty)
        {
            throw new InventoryRequestException("The server returned an empty response.");
        }
        return result!;
    }

}
