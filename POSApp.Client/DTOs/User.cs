using System.ComponentModel.DataAnnotations;
using POSApp.Client.Security;

namespace POSApp.Client.DTOs;

public sealed class UserEditorInput
{
    [Required, StringLength(256)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = AppRoles.SalesAssociate;

    public string? Password { get; set; }
}

public sealed record UserListItem(int Id, string Username, string[] Roles);
