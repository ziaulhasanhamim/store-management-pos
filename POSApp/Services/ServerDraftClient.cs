using POSApp.Client.Services;

namespace POSApp.Services;

public sealed class ServerDraftClient : IDraftClient, IDisposable
{
    private readonly DraftService drafts;
    private readonly AuthenticationStateProvider authentication;
    private Task<AuthenticationState>? state;

    public ServerDraftClient(DraftService drafts, AuthenticationStateProvider authentication)
    {
        this.drafts = drafts;
        this.authentication = authentication;
        authentication.AuthenticationStateChanged += StateChanged;
    }

    private void StateChanged(Task<AuthenticationState> current) => state = current;
    private Task<AuthenticationState> StateAsync() => state ?? authentication.GetAuthenticationStateAsync();

    public async Task<string> OwnerAsync() =>
        (await StateAsync()).User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";

    public async Task<EditorDraftDocument?> ReadAsync(string kind) =>
        await drafts.ReadAsync((await StateAsync()).User, kind);

    public async Task<EditorDraftDocument> SaveAsync(string kind, EditorDraftInput input) =>
        await drafts.SaveAsync((await StateAsync()).User, kind, input);

    public void Dispose() => authentication.AuthenticationStateChanged -= StateChanged;
}
