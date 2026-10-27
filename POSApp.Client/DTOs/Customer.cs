namespace POSApp.Client.DTOs;

public sealed class CustomerCreateInput
{
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public decimal OpeningBalance { get; set; }
}

public sealed record CustomerRow(int Id, string Name, string? Phone, decimal OpeningBalance, decimal Balance);
public sealed record CustomerLedgerRow(DateTimeOffset At, string Type, decimal Amount, decimal BalanceAfter, string? Notes);
public sealed record CustomerAccount(CustomerRow Customer, IReadOnlyList<CustomerLedgerRow> History)
{
    public PageInfo? Pagination { get; init; }
}

public sealed record CustomerDirectory(PagedResult<CustomerRow> Listing, decimal Due, decimal Credit);
