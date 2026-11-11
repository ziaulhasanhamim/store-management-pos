using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public static class FinancialPosting
{
    public static async Task PostAsync(AppDbContext db, int actorId, PaymentMethod method, decimal amount,
        CashMovementKind kind, string notes, DateOnly date, CancellationToken ct,
        Payment? payment = null, SupplierPayment? supplierPayment = null, Expense? expense = null, PayrollEntry? payroll = null,
        int? cashLocationId = null, CashSession? cashSession = null, bool periodValidated = false, DebtEntry? debtEntry = null,
        DateTimeOffset? occurredAt = null)
    {
        if (!periodValidated)
        {
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
        }
        var timestamp = occurredAt?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        if (method == PaymentMethod.Cash)
        {
            var drawer = await CashDrawerService.RequireDrawerAsync(db, actorId, ct, cashLocationId);
            CashDrawerService.Append(db, drawer, new CashMovement
            {
                RequestId = Guid.NewGuid(),
                BusinessDate = date,
                CreatedAt = timestamp,
                CashSession = cashSession,
                CashSessionId = cashSession?.Id,
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
                CreatedAt = timestamp,
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
                CreatedAt = timestamp,
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

    public static Task ReverseClearingAsync(AppDbContext db, int saleId, int actorId, string reason, DateOnly activeDate, CancellationToken ct)
        => ReverseClearingAsync(db, saleId, actorId, reason, activeDate, null, ct);

    public static async Task ReverseClearingAsync(AppDbContext db, int saleId, int actorId, string reason, DateOnly activeDate, IConfiguration? configuration, CancellationToken ct)
    {
        var entries = await db.PaymentClearingEntries
            .Include(entry => entry.Payout)
                .ThenInclude(payout => payout!.Expense)
            .Where(entry => entry.Payment!.SaleId == saleId
                && (entry.Status == PaymentClearingStatus.Unsettled || entry.Status == PaymentClearingStatus.Settled))
            .ToListAsync(ct);

        await ReverseClearingEntriesAsync(db, entries, $"sale #{saleId}", actorId, reason, activeDate, configuration, ct);
    }

    public static async Task ReverseClearingByPaymentAsync(AppDbContext db, int paymentId, int actorId, string reason, DateOnly activeDate, IConfiguration? configuration, CancellationToken ct)
    {
        var entries = await db.PaymentClearingEntries
            .Include(entry => entry.Payout)
                .ThenInclude(payout => payout!.Expense)
            .Where(entry => entry.PaymentId == paymentId
                && (entry.Status == PaymentClearingStatus.Unsettled || entry.Status == PaymentClearingStatus.Settled))
            .ToListAsync(ct);

        await ReverseClearingEntriesAsync(db, entries, $"payment #{paymentId}", actorId, reason, activeDate, configuration, ct);
    }

    private static async Task ReverseClearingEntriesAsync(
        AppDbContext db,
        IReadOnlyList<PaymentClearingEntry> entries,
        string sourceLabel,
        int actorId,
        string reason,
        DateOnly activeDate,
        IConfiguration? configuration,
        CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            if (entry.Status == PaymentClearingStatus.Settled && entry.Payout is not null)
            {
                var payout = entry.Payout;
                var entryFee = payout.GrossAmount > 0
                    ? Math.Round(payout.FeeAmount * (entry.GrossAmount / payout.GrossAmount), 2)
                    : 0m;
                var entryNet = entry.GrossAmount - entryFee;

                // 1. Standalone gross reversal in bank (-entry.GrossAmount).
                // Never set ReversesMovementId = payout.BankMovementId because a batch payout
                // may contain multiple sales: hiding payout.BankMovementId would suppress the entire
                // batch from DailyFinanceBuilder and crash on a 2nd void due to IX_BankMovements_ReversesMovementId.
                var grossReversal = new BankMovement
                {
                    RequestId = Guid.NewGuid(),
                    BusinessDate = activeDate,
                    Method = PaymentMethod.BankTransfer,
                    Kind = CashMovementKind.Payout,
                    Amount = -entry.GrossAmount,
                    Notes = $"Reversal of clearing payout #{payout.Id} for voided {sourceLabel}: {reason}".Trim(),
                    CreatedByUserId = actorId
                };
                await BankAsync(db, grossReversal, ct);

                // 2. If a processing fee was deducted, post a standalone fee reversal expense (-entryFee)
                // and corresponding bank credit (+entryFee) without deleting or mutating the batch fee Expense.
                Expense? feeReversalExpense = null;
                if (entryFee > 0)
                {
                    var feeCategoryId = payout.Expense?.CategoryDefinitionId;
                    if (feeCategoryId is null)
                    {
                        var feeCategory = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Name == "Payment Processing Fees", ct);
                        if (feeCategory is null)
                        {
                            feeCategory = new ExpenseCategoryDefinition
                            {
                                Name = "Payment Processing Fees",
                                NormalizedName = NameNormalization.Normalize("Payment Processing Fees"),
                                IsArchived = false
                            };
                            db.ExpenseCategories.Add(feeCategory);
                            await db.SaveChangesAsync(ct);
                        }
                        feeCategoryId = feeCategory.Id;
                    }

                    feeReversalExpense = new Expense
                    {
                        RequestId = Guid.NewGuid(),
                        StockLocationId = entry.StockLocationId ?? payout.StockLocationId,
                        CategoryDefinitionId = feeCategoryId,
                        Amount = -entryFee,
                        Method = PaymentMethod.BankTransfer,
                        OccurredAt = AccountingCalendar.At(activeDate, configuration),
                        ReceiptReference = $"REV-PAYOUT-{payout.Id}",
                        Notes = $"Clearing fee credit for voided {sourceLabel} (payout #{payout.Id}): {reason}".Trim(),
                        CreatedByUserId = actorId
                    };
                    db.Expenses.Add(feeReversalExpense);

                    var feeReversal = new BankMovement
                    {
                        RequestId = Guid.NewGuid(),
                        BusinessDate = activeDate,
                        Method = PaymentMethod.BankTransfer,
                        Kind = CashMovementKind.Expense,
                        Amount = entryFee,
                        Expense = feeReversalExpense,
                        Notes = $"Reversal of clearing fee for voided {sourceLabel}: {reason}".Trim(),
                        CreatedByUserId = actorId
                    };
                    await BankAsync(db, feeReversal, ct);
                }

                // 3. Record standalone reversal in PaymentPayouts history
                var reversalPayout = new PaymentPayout
                {
                    RequestId = Guid.NewGuid(),
                    BusinessDate = activeDate,
                    SettledAt = DateTimeOffset.UtcNow,
                    Method = entry.Method,
                    StockLocationId = entry.StockLocationId ?? payout.StockLocationId,
                    GrossAmount = -entry.GrossAmount,
                    NetAmount = -entryNet,
                    FeeAmount = -entryFee,
                    BankAccountId = grossReversal.BankAccountId,
                    BankMovement = grossReversal,
                    Expense = feeReversalExpense,
                    Reference = $"REV-PAYOUT-{payout.Id}",
                    Notes = $"Reversal of payout #{payout.Id} for voided {sourceLabel}: {reason}".Trim(),
                    CreatedByUserId = actorId
                };
                db.PaymentPayouts.Add(reversalPayout);
            }

            entry.Status = PaymentClearingStatus.Reversed;
            entry.Notes = string.IsNullOrWhiteSpace(entry.Notes) ? reason : $"{entry.Notes} | {reason}";
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            entry.UpdatedByUserId = actorId;
        }
    }
}
