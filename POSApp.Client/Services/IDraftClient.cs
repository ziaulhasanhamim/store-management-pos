using POSApp.Client.DTOs;

namespace POSApp.Client.Services;

public interface IDraftClient
{
    Task<string> OwnerAsync();
    Task<EditorDraftDocument?> ReadAsync(string kind);
    Task<EditorDraftDocument> SaveAsync(string kind, EditorDraftInput input);
}
