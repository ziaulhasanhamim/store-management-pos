using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public interface IImportClient
{
    Task<ImportStatus> EnqueueAsync(string kind, ImportInput input);
    Task<IReadOnlyList<ImportStatus>> ReadAsync(string kind, CancellationToken ct = default);
}

public sealed class HttpImportClient(HttpReceivingClient http) : IImportClient
{
    public Task<ImportStatus> EnqueueAsync(string kind, ImportInput input) =>
        http.WriteAsync<ImportInput, ImportStatus>($"api/imports/{kind}", input);

    public Task<IReadOnlyList<ImportStatus>> ReadAsync(string kind, CancellationToken ct = default) =>
        http.ReadAsync<IReadOnlyList<ImportStatus>>($"api/imports/{kind}", ct);
}
