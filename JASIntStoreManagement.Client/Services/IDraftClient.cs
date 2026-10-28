using JASIntStoreManagement.Client.DTOs;

namespace JASIntStoreManagement.Client.Services;

public interface IDraftClient
{
    Task<string> OwnerAsync();
    Task<EditorDraftDocument?> ReadAsync(string kind);
    Task<EditorDraftDocument> SaveAsync(string kind, EditorDraftInput input);
}
