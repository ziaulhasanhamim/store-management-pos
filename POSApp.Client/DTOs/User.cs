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

    public bool MustChangePassword { get; set; }

    public int? CashLocationId { get; set; }
}

public sealed record UserListItem(int Id, string Username, string[] Roles, bool MustChangePassword = false)
{
    public int? CashLocationId { get; init; }
    public string? CashLocationName { get; init; }
}

public sealed record RolePermissionMatrixRow(
    string PolicyName,
    string Title,
    string Description,
    bool CashierGranted,
    bool SalesAssociateGranted,
    bool InventoryViewerGranted);

public sealed class ChangePasswordInput
{
    [Required(ErrorMessage = "Enter your current password.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a new password of at least 8 characters.")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your new password.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
