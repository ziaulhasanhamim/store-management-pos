namespace POSApp.Client.DTOs;

public sealed class RecordCorrectionInput
{
    public int? PartyId { get; set; }
    public string Reason { get; set; } = "";
    public string ExpectedRevision { get; set; } = "";
}

public sealed record RecordChangeRow(DateTimeOffset At, string Action, string Reason, string Actor);

public sealed record EditableRecord(
    string Kind,
    int Id,
    string Name,
    DateTimeOffset At,
    int? PartyId,
    bool Deleted,
    string Revision,
    IReadOnlyList<Lookup> Parties,
    IReadOnlyList<RecordChangeRow> History)
{
    public decimal? Total { get; init; }
    public decimal? Paid { get; init; }
    public IReadOnlyList<RecordStockEffect> StockEffects { get; init; } = [];
}

public sealed record RecordStockEffect(string Product, decimal Quantity, string Unit);

public sealed record AuditRow(DateTimeOffset At, string Actor, string Kind, int Id,
    string Action, string Reason, string Before, string After);
