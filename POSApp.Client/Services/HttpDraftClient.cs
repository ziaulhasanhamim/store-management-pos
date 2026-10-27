using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public sealed class HttpDraftClient(HttpReceivingClient http) : IDraftClient
{
    public Task<string> OwnerAsync() => http.ReadAsync<string>("api/drafts/identity");

    public Task<EditorDraftDocument?> ReadAsync(string kind) =>
        http.ReadOptionalAsync<EditorDraftDocument>($"api/drafts/{Uri.EscapeDataString(kind)}");

    public Task<EditorDraftDocument> SaveAsync(string kind, EditorDraftInput input) =>
        http.WriteAsync<EditorDraftInput, EditorDraftDocument>($"api/drafts/{Uri.EscapeDataString(kind)}", input, "api/entries/antiforgery");
}
