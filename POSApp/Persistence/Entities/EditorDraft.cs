namespace POSApp.Persistence.Entities;

public sealed class EditorDraft : Entity
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public required string Kind { get; set; }
    public required string Payload { get; set; }
    public long Revision { get; set; }
    public Guid SaveId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
