using System.Text.Json.Serialization;
using POSApp.Client.Enums;

namespace POSApp.Client.DTOs;

public sealed record MoneyEntryInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int? Id { get; set; }
    public long Revision { get; set; }
    public int PartyId { get; set; }
    public int CategoryId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public string Notes { get; set; } = "";
    public string? Receipt { get; set; }
}

public sealed record MoneyEntryCommand(Guid RequestId, DateOnly WorkingDate, IReadOnlyList<MoneyEntryInput> Rows);
public sealed record MoneyEntryResult(IReadOnlyList<int> Ids);
public sealed record ExpenseCategoryOption(int Id, string Name, bool IsArchived);
public sealed record ExpenseCategoryInput(int? Id, string Name, bool IsArchived = false);
public sealed class ExpenseCategoryEditInput
{
    public int? Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsArchived { get; set; }
}
public sealed record EntryParty(int Id, string Name, decimal Balance);
public sealed record MoneyEntryRow(int Id, DateTimeOffset At, int PartyId, string Party,
    int CategoryId, string Category, decimal Amount, PaymentMethod Method, string Notes,
    string? Receipt, long Revision, bool CanEdit, string? LockedReason)
{
    public int? PurchaseId { get; init; }
}
public sealed record MoneyEntryPage(string Kind, DateOnly WorkingDate, DateOnly Date,
    bool CanCreate, bool CanManageCategories, decimal Total, int Count,
    IReadOnlyList<ExpenseCategoryOption> Categories, IReadOnlyList<EntryParty> Parties,
    IReadOnlyList<MoneyEntryRow> Rows)
{
    public PageInfo? Pagination { get; init; }

    [JsonIgnore]
    public IReadOnlyList<DailyExpenseCategory> ExpenseCategories => Rows
        .GroupBy(row => row.CategoryId)
        .Select(group => new DailyExpenseCategory(group.Key, group.First().Category,
            group.Sum(row => row.Amount), group.ToArray()))
        .OrderBy(category => category.Name)
        .ToArray();
}

public sealed record DailyExpenseCategory(int Id, string Name, decimal Amount,
    IReadOnlyList<MoneyEntryRow> Entries);
