namespace POSApp.Persistence.Entities;

public sealed class RolePermission : AuditableEntity
{
    public required string RoleName { get; set; }
    public required string PolicyName { get; set; }
    public bool IsGranted { get; set; }
}
