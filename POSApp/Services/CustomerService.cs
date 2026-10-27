using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed class CustomerService(IServiceScopeFactory scopes)
{
    public async Task<IReadOnlyList<CustomerRow>> ListAsync(ClaimsPrincipal actor, string? search = null, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        return await DirectoryQuery(db, search).ToArrayAsync(ct);
    }

    public async Task<CustomerDirectory> PageAsync(ClaimsPrincipal actor, string? search = null,
        int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        var customers = Filter(db, search);
        var summary = await customers.GroupBy(customer => 1).Select(group => new
        {
            Due = group.Sum(customer => customer.Balance != null && customer.Balance.CurrentBalance > 0
                ? customer.Balance.CurrentBalance : 0),
            Credit = group.Sum(customer => customer.Balance != null && customer.Balance.CurrentBalance < 0
                ? -customer.Balance.CurrentBalance : 0)
        }).SingleOrDefaultAsync(ct);
        return new(await DirectoryQuery(db, search).ToPageAsync(page, pageSize, ct), summary?.Due ?? 0, summary?.Credit ?? 0);
    }

    private static IQueryable<Customer> Filter(AppDbContext db, string? search)
    {
        var query = db.Customers.AsNoTracking().Where(customer => !customer.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var normalizedTerm = NameNormalization.Normalize(term);
            query = query.Where(customer => customer.NormalizedName.Contains(normalizedTerm)
                || customer.Phone != null && customer.Phone.Contains(term));
        }
        return query;
    }

    private static IQueryable<CustomerRow> DirectoryQuery(AppDbContext db, string? search) =>
        Filter(db, search).OrderBy(customer => customer.Id)
            .Select(customer => new CustomerRow(customer.Id, customer.Name, customer.Phone,
                customer.Balance == null ? 0 : customer.Balance.OpeningBalance,
                customer.Balance == null ? 0 : customer.Balance.CurrentBalance));

    public async Task<CustomerAccount?> ReadAsync(ClaimsPrincipal actor, int id, CancellationToken ct = default, int page = 1, int pageSize = 30)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.SalesAssociate, ct);
        var customer = await db.Customers.AsNoTracking().Where(customer => customer.Id == id && !customer.IsDeleted)
            .Select(customer => new CustomerRow(customer.Id, customer.Name, customer.Phone,
                customer.Balance == null ? 0 : customer.Balance.OpeningBalance,
                customer.Balance == null ? 0 : customer.Balance.CurrentBalance)).SingleOrDefaultAsync(ct);
        if (customer is null)
        {
            return null;
        }
        var history = await db.CustomerBalanceEntries.AsNoTracking().Where(entry => entry.CustomerBalance!.CustomerId == id)
            .OrderByDescending(entry => entry.Id)
            .Select(entry => new CustomerLedgerRow(entry.OccurredAt, entry.Type.ToString(), entry.Amount, entry.BalanceAfter, entry.Notes))
            .ToPageAsync(page, pageSize, ct);
        return new(customer, history.Items) { Pagination = history.Pagination };
    }
}
