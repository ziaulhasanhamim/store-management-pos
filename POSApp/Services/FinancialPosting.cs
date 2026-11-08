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

    public static Task ReverseClearingAsync(AppDbContext db, int saleId, int actorId, string reason, DateOnly activeDate, CancellationToken ct)
        => ReverseClearingAsync(db, saleId, actorId, reason, activeDate, null, ct);

    public static async Task ReverseClearingAsync(AppDbContext db, int saleId, int actorId, string reason, DateOnly activeDate, IConfiguration? configuration, CancellationToken ct)
    {
        var entries = await db.PaymentClearingEntries
            .Include(entry => entry.Payout)
            .Where(entry => entry.Payment!.SaleId == saleId
                && (entry.Status == PaymentClearingStatus.Unsettled || entry.Status == PaymentClearingStatus.Settled))
            .ToListAsync(ct);

        foreach (var entry in entries)
        {
            if (entry.Status == PaymentClearingStatus.Settled && entry.Payout is not null)
            {
                var payout = entry.Payout;
                var entryFee = payout.GrossAmount > 0
                    ? Math.Round(payout.FeeAmount * (entry.GrossAmount / payout.GrossAmount), 2)
                    : 0m;
                var entryNet = entry.GrossAmount - entryFee;

                // 1. Reverse gross amount from bank (-entry.GrossAmount)
                var grossReversal = new BankMovement
                {
                    RequestId = Guid.NewGuid(),
                    BusinessDate = activeDate,
                    Method = PaymentMethod.BankTransfer,
                    Kind = CashMovementKind.Payout,
                    Amount = -entry.GrossAmount,
                    Notes = $"Reversal of clearing payout #{payout.Id} for voided sale #{saleId}: {reason}".Trim(),
                    ReversesMovementId = payout.BankMovementId,
                    CreatedByUserId = actorId
                };
                await BankAsync(db, grossReversal, ct);

                // 2. If fee was deducted, reverse the fee from bank (+entryFee)
                if (entryFee > 0)
                {
                    var feeReversal = new BankMovement
                    {
                        RequestId = Guid.NewGuid(),
                        BusinessDate = activeDate,
                        Method = PaymentMethod.BankTransfer,
                        Kind = CashMovementKind.Reversal,
                        Amount = entryFee,
                        Notes = $"Reversal of clearing fee for voided sale #{saleId}: {reason}".Trim(),
                        CreatedByUserId = actorId
                    };
                    await BankAsync(db, feeReversal, ct);
                }

                // 3. Void/adjust the fee Expense if it occurred in the same working day
                if (payout.ExpenseId is not null)
                {
                    var expense = await db.Expenses.FindAsync([payout.ExpenseId.Value], ct);
                    if (expense is not null)
                    {
                        var expDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(expense.OccurredAt, AccountingCalendar.Zone(configuration)).DateTime);
                        if (expDate == activeDate)
                        {
                            if (entryFee >= expense.Amount)
                            {
                                var linkedBankMoves = await db.BankMovements.Where(m => m.ExpenseId == expense.Id).ToListAsync(ct);
                                foreach (var m in linkedBankMoves)
                                {
                                    m.ExpenseId = null;
                                }
                                payout.ExpenseId = null;
                                db.RecordChanges.Add(new RecordChange
                                {
                                    RequestId = Guid.NewGuid(),
                                    RecordType = "expense",
                                    RecordId = expense.Id,
                                    Action = "Delete",
                                    Reason = $"Voided fee expense #{expense.Id} due to voided sale #{saleId}",
                                    Before = JsonSerializer.Serialize(new { expense.CategoryDefinitionId, expense.Amount, expense.Method, expense.Notes }),
                                    After = "null",
                                    CreatedByUserId = actorId
                                });
                                db.Expenses.Remove(expense);
                            }
                            else
                            {
                                expense.Amount -= entryFee;
                                expense.Notes = $"{expense.Notes} | Reduced by {entryFee:N2} due to voided sale #{saleId}";
                            }
                        }
                    }
                }

                // 4. Record reversal in PaymentPayouts history
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
                    Expense = null,
                    Reference = $"REV-PAYOUT-{payout.Id}",
                    Notes = $"Reversal of payout #{payout.Id} for voided sale #{saleId}: {reason}".Trim(),
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
