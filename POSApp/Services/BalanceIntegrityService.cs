using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed record BalanceIssue(string Account, string Reference, decimal Stored, decimal Expected);

public sealed class BalanceIntegrityService(IServiceScopeFactory scopes)
{
    // Closing only checks the affected drawer, using its indexed movement relation.
    public static async Task CheckDrawerAsync(AppDbContext db, CashSession session, CancellationToken ct)
    {
        var movementTotal = await db.CashMovements.Where(move => move.CashSessionId == session.Id)
            .Select(move => (decimal?)move.Amount).SumAsync(ct) ?? 0;
        if (session.ExpectedCash != session.OpeningCash + movementTotal)
        {
            throw new System.ComponentModel.DataAnnotations.ValidationException(
                "This drawer's expected cash does not match its movements. Ask an administrator to review Balance checks before closing.");
        }
    }

    // Full history scans are deliberately on-demand, never part of normal saves.
    public Task<IReadOnlyList<BalanceIssue>> ReadAsync(ClaimsPrincipal actor, CancellationToken ct = default)
    {
        return StoreOperation.RunAsync<IReadOnlyList<BalanceIssue>>(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var issues = new List<BalanceIssue>();
            var suppliers = await db.SupplierBalances.AsNoTracking().Select(balance => new
            {
                balance.SupplierId,
                balance.CurrentBalance,
                Latest = (balance.Entries.OrderBy(entry => entry.Id)
                    .Select(entry => (decimal?)(entry.BalanceAfter - entry.Amount)).FirstOrDefault() ?? balance.OpeningBalance)
                    + (balance.Entries.Select(entry => (decimal?)entry.Amount).Sum() ?? 0)
            }).ToArrayAsync(ct);
            foreach (var row in suppliers.Where(row => row.Latest != row.CurrentBalance))
            {
                issues.Add(new("Supplier", $"Supplier #{row.SupplierId}", row.CurrentBalance, row.Latest));
            }
            var customers = await db.CustomerBalances.AsNoTracking().Select(balance => new
            {
                balance.CustomerId,
                balance.CurrentBalance,
                Latest = (balance.Entries.OrderBy(entry => entry.Id)
                    .Select(entry => (decimal?)(entry.BalanceAfter - entry.Amount)).FirstOrDefault() ?? balance.OpeningBalance)
                    + (balance.Entries.Select(entry => (decimal?)entry.Amount).Sum() ?? 0)
            }).ToArrayAsync(ct);
            foreach (var row in customers.Where(row => row.Latest != row.CurrentBalance))
            {
                issues.Add(new("Customer", $"Customer #{row.CustomerId}", row.CurrentBalance, row.Latest));
            }
            var lenders = await db.LenderBalances.AsNoTracking().Select(balance => new
            {
                balance.LenderId,
                balance.CurrentBalance,
                Latest = (balance.Entries.OrderBy(entry => entry.Id)
                    .Select(entry => (decimal?)(entry.BalanceAfter - entry.Amount)).FirstOrDefault() ?? balance.OpeningBalance)
                    + (balance.Entries.Select(entry => (decimal?)entry.Amount).Sum() ?? 0)
            }).ToArrayAsync(ct);
            foreach (var row in lenders.Where(row => row.Latest != row.CurrentBalance))
            {
                issues.Add(new("Lender", $"Lender #{row.LenderId}", row.CurrentBalance, row.Latest));
            }
            var banks = await db.BankAccounts.AsNoTracking().Select(bank => new
            {
                bank.Code,
                bank.CurrentBalance,
                Latest = (db.BankMovements.Where(move => move.BankAccountId == bank.Id)
                    .OrderBy(move => move.Id).Select(move => (decimal?)(move.BalanceAfter - move.Amount)).FirstOrDefault() ?? bank.OpeningBalance)
                    + (db.BankMovements.Where(move => move.BankAccountId == bank.Id).Select(move => (decimal?)move.Amount).Sum() ?? 0)
            }).ToArrayAsync(ct);
            foreach (var row in banks.Where(row => row.Latest != row.CurrentBalance))
            {
                issues.Add(new("Bank", row.Code, row.CurrentBalance, row.Latest));
            }
            var quantities = await db.InventoryBalances.AsNoTracking().Select(balance => new
            {
                balance.ProductId,
                balance.StockLocationId,
                balance.QuantityOnHand,
                Expected = db.InventoryMovements.Where(move => move.ProductId == balance.ProductId
                        && move.StockLocationId == balance.StockLocationId)
                    .Select(move => (decimal?)move.Quantity).Sum() ?? 0
            }).ToArrayAsync(ct);
            foreach (var row in quantities.Where(row => row.QuantityOnHand != row.Expected))
            {
                issues.Add(new("Stock quantity", $"Item #{row.ProductId} · location #{row.StockLocationId}", row.QuantityOnHand, row.Expected));
            }
            var sessions = await db.CashSessions.AsNoTracking().Where(session => session.ClosedAt == null)
                .Select(session => new
                {
                    session.Id,
                    session.ExpectedCash,
                    Expected = session.OpeningCash + (db.CashMovements.Where(move => move.CashSessionId == session.Id)
                        .Select(move => (decimal?)move.Amount).Sum() ?? 0)
                }).ToArrayAsync(ct);
            foreach (var row in sessions.Where(row => row.ExpectedCash != row.Expected))
            {
                issues.Add(new("Cash drawer", $"Session #{row.Id}", row.ExpectedCash, row.Expected));
            }
            var missingBalances = await db.InventoryMovements.AsNoTracking()
                .Where(move => !db.InventoryBalances.Any(balance => balance.ProductId == move.ProductId
                    && balance.StockLocationId == move.StockLocationId))
                .GroupBy(move => new { move.ProductId, move.StockLocationId })
                .Select(group => new { group.Key.ProductId, group.Key.StockLocationId, Quantity = group.Sum(move => move.Quantity) })
                .Where(row => row.Quantity != 0).ToArrayAsync(ct);
            foreach (var row in missingBalances)
            {
                issues.Add(new("Missing stock balance", $"Item #{row.ProductId} · location #{row.StockLocationId}", 0, row.Quantity));
            }
            var advances = await db.StaffAccounts.AsNoTracking().Select(account => new
            {
                account.EmployeeId,
                account.OutstandingAdvance,
                Expected = (account.Entries.OrderBy(entry => entry.Id)
                    .Select(entry => (decimal?)(entry.AdvanceBalanceAfter - entry.AdvanceCreated + entry.AdvanceApplied))
                    .FirstOrDefault() ?? 0)
                    + (account.Entries.Select(entry => (decimal?)(entry.AdvanceCreated - entry.AdvanceApplied)).Sum() ?? 0)
            }).ToArrayAsync(ct);
            foreach (var row in advances.Where(row => row.OutstandingAdvance != row.Expected))
            {
                issues.Add(new("Employee advance", $"Employee #{row.EmployeeId}", row.OutstandingAdvance, row.Expected));
            }
            return issues;
        }, ct);
    }
}
