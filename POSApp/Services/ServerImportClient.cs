using System.Security.Claims;
using POSApp.Client.Services;

namespace POSApp.Services;

public sealed class ServerImportClient : IImportClient, IDisposable
{
    private readonly ImportService imports;
    private readonly AuthenticationStateProvider authentication;
    private Task<AuthenticationState>? state;

    public ServerImportClient(ImportService imports, AuthenticationStateProvider authentication)
    {
        this.imports = imports;
        this.authentication = authentication;
        authentication.AuthenticationStateChanged += OnAuthenticationChanged;
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> updated) => state = updated;

    private async Task<ClaimsPrincipal> ActorAsync() =>
        (await (state ??= authentication.GetAuthenticationStateAsync())).User;

    public async Task<ImportStatus> EnqueueAsync(string kind, ImportInput input) =>
        await imports.EnqueueAsync(await ActorAsync(), kind, input);

    public async Task<IReadOnlyList<ImportStatus>> ReadAsync(string kind, CancellationToken ct = default) =>
        await imports.ReadAsync(await ActorAsync(), kind, ct);

    public void Dispose() => authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
}
