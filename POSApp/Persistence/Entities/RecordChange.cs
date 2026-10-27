namespace POSApp.Persistence.Entities;

public sealed class RecordChange : AuditableEntity
{
    public Guid? RequestId { get; set; }
    public required string RecordType { get; set; }
    public int RecordId { get; set; }
    public required string Action { get; set; }
    public required string Reason { get; set; }
    public required string Before { get; set; }
    public required string After { get; set; }
}
