namespace POSApp.Persistence.Entities;

public sealed class ImportJob
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public string? ActiveKey { get; set; }
    public string Fingerprint { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public string? Message { get; set; }
    public string? Result { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
}
