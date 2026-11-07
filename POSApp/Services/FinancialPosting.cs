using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public static class FinancialPosting
{
    public static async Task PostAsync(AppDbContext db, int actorId, PaymentMethod method, decimal amount,
        CashMovementKind kind, string notes, DateOnly date, CancellationToken ct,
        Payment? payment = null, SupplierPayment? supplierPayment = null, Expense? expense = null, PayrollEntry? payroll = null,
        int? cashLocationId = null, CashSession? cashSession = null, bool periodValidated = false, DebtEntry? debtEntry = null)
    {
        if (!periodValidated)
        {
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
        }
        if (method == PaymentMethod.Cash)
        {
            var session = cashSession ?? await CashDrawerService.RequireOpenAsync(db, actorId, ct, cashLocationId);
            if (session.ClosedAt is not null || session.BusinessDate != date)
            {
                throw new System.ComponentModel.DataAnnotations.ValidationException("Use an open cash drawer for the selected working day.");
            }
            CashDrawerService.Append(db, session, new CashMovement
            {
                RequestId = Guid.NewGuid(),
                BusinessDate = date,
                Kind = kind,
                Amount = amount,
                Notes = notes,
                Payment = payment,
                SupplierPayment = supplierPayment,
                Expense = expense,
                PayrollEntry = payroll,
                DebtEntry = debtEntry,
                CreatedByUserId = actorId
            });
        }
        else if (kind == CashMovementKind.Sale && (method == PaymentMethod.Card || method == PaymentMethod.MobileWallet))
        {
            var locationId = cashLocationId
                ?? payment?.Sale?.StockLocationId
                ?? (payment?.SaleId != null ? await db.Sales.Where(s => s.Id == payment.SaleId).Select(s => (int?)s.StockLocationId).FirstOrDefaultAsync(ct) : null)
                ?? await db.Users.Where(u => u.Id == actorId).Select(u => u.CashLocationId).FirstOrDefaultAsync(ct);

            db.PaymentClearingEntries.Add(new PaymentClearingEntry
            {
                RequestId = Guid.NewGuid(),
                Payment = payment,
                PaymentId = payment?.Id ?? 0,
                StockLocationId = locationId,
                BusinessDate = date,
                Method = method,
                GrossAmount = amount,
                SettledAmount = 0m,
                Status = PaymentClearingStatus.Unsettled,
                Notes = notes,
                CreatedByUserId = actorId
            });
        }
        else
        {
            await BankAsync(db, new BankMovement
            {
                RequestId = Guid.NewGuid(),
                BusinessDate = date,
                Method = method,
                Kind = kind,
                Amount = amount,
                Notes = notes,
                Payment = payment,
                SupplierPayment = supplierPayment,
                Expense = expense,
                PayrollEntry = payroll,
                DebtEntry = debtEntry,
                CreatedByUserId = actorId
            }, ct);
        }
    }

    public static async Task BankAsync(AppDbContext db, BankMovement movement, CancellationToken ct)
    {
        var account = db.BankAccounts.Local.SingleOrDefault(account => account.Code == "MAIN")
            ?? await db.BankAccounts.SingleOrDefaultAsync(account => account.Code == "MAIN", ct);
        if (account is null)
        {
            account = new BankAccount();
            db.BankAccounts.Add(account);
        }
        account.CurrentBalance += movement.Amount;
        StoreAccess.Money(account.CurrentBalance, "Bank balance", negative: true);
        account.UpdatedAt = DateTimeOffset.UtcNow;
        account.UpdatedByUserId = movement.CreatedByUserId;
        movement.BankAccount = account;
        movement.BalanceAfter = account.CurrentBalance;
        db.BankMovements.Add(movement);
    }

    public static async Task ReverseBankAsync(AppDbContext db, string kind, int id, int actorId, string reason, DateOnly activeDate, CancellationToken ct)
    {
        var originals = await db.BankMovements.Where(move => kind == "sale" ? move.Payment!.SaleId == id : move.SupplierPayment!.PurchaseId == id)
            .Where(move => move.ReversesMovementId == null
                && !db.BankMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
            .ToListAsync(ct);
        foreach (var movement in originals)
        {
            var businessDateToUse = movement.BusinessDate == activeDate ? movement.BusinessDate : activeDate;
            await AccountingCalendar.EnsureOpenAsync(db, businessDateToUse, ct);
            await BankAsync(db, new BankMovement
            {
                RequestId = Guid.NewGuid(),
                BusinessDate = businessDateToUse,
                Kind = CashMovementKind.Reversal,
                Method = movement.Method,
                Amount = -movement.Amount,
                Notes = reason,
                ReversesMovementId = movement.Id,
                CreatedByUserId = actorId
            }, ct);
        }
    }

    public static async Task ReverseClearingAsync(AppDbContext db, int saleId, int actorId, string reason, DateOnly activeDate, CancellationToken ct)
    {
        var entries = await db.PaymentClearingEntries
            .Where(entry => entry.Payment!.SaleId == saleId && entry.Status == PaymentClearingStatus.Unsettled)
            .ToListAsync(ct);
        foreach (var entry in entries)
        {
            entry.Status = PaymentClearingStatus.Reversed;
            entry.Notes = string.IsNullOrWhiteSpace(entry.Notes) ? reason : $"{entry.Notes} | {reason}";
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            entry.UpdatedByUserId = actorId;
        }
    }
}
