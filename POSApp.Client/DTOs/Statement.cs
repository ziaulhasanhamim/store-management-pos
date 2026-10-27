namespace POSApp.Client.DTOs;

public sealed record StatementRow(DateOnly Date, DateTimeOffset PostedAt, string Type, string Reference,
    string Notes, decimal Amount, decimal Balance);

public sealed record BalanceStatement(string Account, ReportRange Range, IReadOnlyList<Lookup> Drawers,
    int? DrawerId, decimal Opening, decimal MoneyIn, decimal MoneyOut, decimal Closing,
    decimal OwnerContributions, decimal OwnerWithdrawals, IReadOnlyList<StatementRow> Rows, int Count, int Page)
{
    public decimal PageOpening { get; init; }
}
