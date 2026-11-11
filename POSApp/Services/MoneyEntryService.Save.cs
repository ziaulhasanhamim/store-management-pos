using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;

namespace POSApp.Services;

public sealed partial class MoneyEntryService
{
    public async Task<MoneyEntryResult> SaveAsync(ClaimsPrincipal actor, string kind, MoneyEntryCommand input, CancellationToken ct = default)
    {
        // Interactive server events can continue while a save awaits I/O. Freeze
        // the submitted rows once, so every retry applies the same values.
        if (input.Rows is not null)
        {
            input = input with
            {
                Rows = input.Rows.Select(row => row is null ? null! : row with { }).ToArray()
            };
        }
        return await StoreOperation.RunAsync<MoneyEntryResult>(async () =>
        {
            var requiredRole = Role(kind);
            if (input.RequestId == Guid.Empty || input.Rows is null || input.Rows.Count is 0 or > 100
                || input.Rows.Any(row => row is null || row.RequestId == Guid.Empty)
                || input.Rows.Select(row => row.RequestId).Distinct().Count() != input.Rows.Count
                || input.Rows.Where(row => row.Id != null).Select(row => row.Id).Distinct().Count() != input.Rows.Count(row => row.Id != null))
            {
                throw new ValidationException("Add 1–100 distinct entries and try again.");
            }
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, requiredRole, ct);
            var admin = await CashDrawerService.IsAdminAsync(db, actorId, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var payloadHash = "v2:" + StoreOperation.Fingerprint(input);
            var previous = await db.FinancialEntryBatches.SingleOrDefaultAsync(batch => batch.RequestId == input.RequestId, ct);
            if (previous is not null)
            {
                if (previous.CreatedByUserId != actorId || previous.Kind != kind)
                {
                    throw new UnauthorizedAccessException();
                }
                var cached = JsonSerializer.Deserialize<CachedBatch>(previous.ResultJson)!;
                var matches = cached.Hash.StartsWith("v2:", StringComparison.Ordinal)
                    ? cached.Hash == payloadHash
                    : cached.Hash == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
                if (!matches)
                {
                    throw new ValidationException("The original batch was already saved. Refresh the page before entering different values.");
                }
                return new(cached.Ids);
            }
            var date = input.WorkingDate == default ? await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct) : input.WorkingDate;
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            var at = AccountingCalendar.At(date, configuration);
            var partyIds = input.Rows.Select(row => row.PartyId).Distinct().ToArray();
            var activeParties = new HashSet<int>();
            if (kind == "supplier-payments")
            {
                activeParties = (await db.Suppliers.Where(party => partyIds.Contains(party.Id) && !party.IsDeleted)
                    .Select(party => party.Id).ToArrayAsync(ct)).ToHashSet();
                await db.SupplierBalances.Where(balance => partyIds.Contains(balance.SupplierId)).LoadAsync(ct);
            }
            else if (kind == "customer-payments")
            {
                activeParties = (await db.Customers.Where(party => partyIds.Contains(party.Id) && !party.IsDeleted)
                    .Select(party => party.Id).ToArrayAsync(ct)).ToHashSet();
                await db.CustomerBalances.Where(balance => partyIds.Contains(balance.CustomerId)).LoadAsync(ct);
            }
            else
            {
                var categoryIds = input.Rows.Select(row => row.CategoryId).Distinct().ToArray();
                await db.ExpenseCategories.Where(category => categoryIds.Contains(category.Id)).LoadAsync(ct);
            }
            var posting = new BatchPosting(db, actorId, date, ct);
            var entities = new List<AuditableEntity>();
            foreach (var row in input.Rows)
            {
                StoreAccess.Money(row.Amount, "Amount");
                if (row.Amount <= 0 || !Enum.IsDefined(row.Method) || row.Notes?.Length > 2000 || row.Receipt?.Length > 200)
                {
                    throw new ValidationException("Enter a positive amount, valid payment method and notes up to 2,000 characters.");
                }
                AuditableEntity? entity = null;
                if (row.Id is not null)
                {
                    var id = row.Id.Value;
                    entity = kind switch
                    {
                        "expenses" => await db.Expenses.FindAsync([id], ct),
                        "supplier-payments" => await db.SupplierPayments.Include(payment => payment.SupplierBalance).Include(payment => payment.Purchase)
                            .SingleOrDefaultAsync(payment => payment.Id == id && (payment.PurchaseId == null || !payment.Purchase!.IsDeleted), ct),
                        _ => await db.Payments.Include(payment => payment.CustomerBalance)
                            .SingleOrDefaultAsync(payment => payment.Id == id && payment.SaleId == null, ct)
                    };
                    if (entity is null)
                    {
                        throw new ValidationException("Entry not found. Linked invoice payments must be edited through their sale or receiving.");
                    }
                    if (entity is Expense { IsVoided: true } or SupplierPayment { IsVoided: true } or Payment { IsVoided: true })
                    {
                        throw new ValidationException("Voided entries cannot be edited.");
                    }
                    if (entity is Expense existingExpense
                        && (existingExpense.Amount <= 0 || await db.PaymentPayouts.AnyAsync(p => p.ExpenseId == existingExpense.Id, ct)))
                    {
                        throw new ValidationException("Clearing payout fee expenses cannot be edited directly.");
                    }
                    if (entity is Payment existingPayment
                        && await db.PaymentClearingEntries.AnyAsync(e => e.PaymentId == existingPayment.Id && e.Status == PaymentClearingStatus.Settled, ct))
                    {
                        throw new ValidationException("This card or wallet collection has already been settled in a bank payout. Void it instead of editing.");
                    }
                    if (!admin && entity.CreatedByUserId != actorId)
                    {
                        throw new UnauthorizedAccessException();
                    }
                    if ((entity.UpdatedAt ?? entity.CreatedAt).UtcTicks != row.Revision)
                    {
                        throw new ValidationException("An entry has changed since you loaded it. Refresh before editing.");
                    }
                    var occurred = entity switch
                    {
                        Expense expense => expense.OccurredAt,
                        SupplierPayment payment => payment.PaidAt,
                        Payment payment => payment.PaidAt,
                        _ => throw new ValidationException("Invalid entry.")
                    };
                    if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(occurred, AccountingCalendar.Zone(configuration)).DateTime) != date)
                    {
                        throw new ValidationException("Only entries in the current open working day can be edited.");
                    }
                }
                var before = entity is null ? null : Snapshot(entity);
                var notes = row.Notes?.Trim() ?? "";
                var oldEntity = entity;
                int? locationId = row.StockLocationId;
                if (oldEntity is not null)
                {
                    if (oldEntity is Expense oldExp)
                    {
                        locationId ??= oldExp.StockLocationId;
                    }
                    locationId ??= await db.CashMovements.Where(move =>
                            (kind == "expenses" ? move.ExpenseId == oldEntity.Id : kind == "supplier-payments" ? move.SupplierPaymentId == oldEntity.Id : move.PaymentId == oldEntity.Id)
                            && move.ReversesMovementId == null && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
                        .Select(move => (int?)(move.CashDrawer != null ? move.CashDrawer.StockLocationId : (move.CashSession != null ? move.CashSession.StockLocationId : null))).FirstOrDefaultAsync(ct);
                    if (kind != "customer-payments")
                    {
                        await ReversePostingsAsync(db, kind, oldEntity.Id, actorId, date, ct);
                    }
                }
                locationId ??= await db.Users.Where(u => u.Id == actorId).Select(u => u.CashLocationId).FirstOrDefaultAsync(ct);
                var rowAt = row.TransactionTime.HasValue ? row.TransactionTime.Value.ToUniversalTime() : at;
                var rowDate = row.TransactionTime.HasValue
                    ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(rowAt, AccountingCalendar.Zone(configuration)).DateTime)
                    : date;
                if (kind == "expenses")
                {
                    var category = db.ExpenseCategories.Local.SingleOrDefault(category => category.Id == row.CategoryId)
                        ?? throw new ValidationException("Select an expense category.");
                    var expense = entity as Expense;
                    if (category.IsArchived && expense?.CategoryDefinitionId != category.Id)
                    {
                        throw new ValidationException("Choose an active expense category.");
                    }
                    if (expense is null)
                    {
                        expense = new Expense { RequestId = row.RequestId, OccurredAt = rowAt, Notes = notes, CreatedByUserId = actorId };
                        db.Expenses.Add(expense);
                    }
                    expense.CategoryDefinitionId = category.Id;
                    expense.Category = category.BuiltInCategory ?? ExpenseCategory.Other;
                    expense.StockLocationId = locationId;
                    expense.Amount = row.Amount;
                    expense.Method = row.Method;
                    expense.Notes = notes;
                    expense.ReceiptReference = row.Receipt?.Trim();
                    await posting.PostAsync(row.Method, -row.Amount, CashMovementKind.Expense,
                        notes.Length == 0 ? category.Name : notes, expense: expense, cashLocationId: locationId, rowDate: rowDate, occurredAt: rowAt);
                    entity = expense;
                }
                else if (kind == "supplier-payments")
                {
                    if (!activeParties.Contains(row.PartyId))
                    {
                        throw new ValidationException("Choose an active supplier.");
                    }
                    var payment = entity as SupplierPayment;
                    if (payment?.PurchaseId is not null
                        && (row.PartyId != payment.SupplierBalance!.SupplierId || row.PartyId != payment.Purchase!.SupplierId))
                    {
                        throw new ValidationException("A receiving payment must stay with its receipt's supplier. Use the receiving correction page to change its supplier.");
                    }
                    if (payment is not null)
                    {
                        var old = payment.SupplierBalance!;
                        old.CurrentBalance += payment.Amount;
                        StoreAccess.Money(old.CurrentBalance, "Supplier balance", negative: true);
                        old.Entries.Add(new SupplierBalanceEntry
                        {
                            SupplierBalanceId = old.Id, Type = BalanceEntryType.Adjustment, Amount = payment.Amount,
                            BalanceAfter = old.CurrentBalance, OccurredAt = payment.PaidAt,
                            Notes = $"Payment edit #{payment.Id}: reverse previous amount", CreatedByUserId = actorId
                        });
                    }
                    var balance = db.SupplierBalances.Local.SingleOrDefault(balance => balance.SupplierId == row.PartyId);
                    if (balance is null)
                    {
                        balance = new SupplierBalance { SupplierId = row.PartyId, OpenedAt = rowAt };
                        db.SupplierBalances.Add(balance);
                    }
                    if (payment is null)
                    {
                        payment = new SupplierPayment
                        {
                            SupplierBalanceId = balance.Id, Amount = row.Amount, PaidAt = rowAt,
                            Reference = $"PAY-{row.RequestId:N}", CreatedByUserId = actorId
                        };
                        db.SupplierPayments.Add(payment);
                    }
                    payment.SupplierBalance = balance;
                    payment.Amount = row.Amount;
                    payment.Method = row.Method;
                    payment.Notes = notes;
                    balance.CurrentBalance -= row.Amount;
                    StoreAccess.Money(balance.CurrentBalance, "Supplier balance", negative: true);
                    balance.UpdatedAt = DateTimeOffset.UtcNow;
                    balance.UpdatedByUserId = actorId;
                    balance.Entries.Add(new SupplierBalanceEntry
                    {
                        SupplierBalanceId = balance.Id, Type = oldEntity is null ? BalanceEntryType.Payment : BalanceEntryType.Adjustment,
                        Amount = -row.Amount, BalanceAfter = balance.CurrentBalance, OccurredAt = payment.PaidAt,
                        SupplierPayment = oldEntity is null ? payment : null,
                        Notes = oldEntity is null ? notes : EditNote("Payment", payment.Id, notes), CreatedByUserId = actorId
                    });
                    await posting.PostAsync(row.Method, -row.Amount, CashMovementKind.SupplierPayment,
                        $"Supplier payment · #{row.PartyId}", supplierPayment: payment, cashLocationId: locationId, rowDate: rowDate, occurredAt: rowAt);
                    entity = payment;
                }
                else
                {
                    if (!activeParties.Contains(row.PartyId))
                    {
                        throw new ValidationException("Choose an active customer.");
                    }
                    var payment = entity as Payment;
                    if (payment is not null)
                    {
                        var old = payment.CustomerBalance!;
                        old.CurrentBalance += payment.Amount;
                        StoreAccess.Money(old.CurrentBalance, "Customer balance", negative: true);
                        old.Entries.Add(new CustomerBalanceEntry
                        {
                            CustomerBalanceId = old.Id, Type = BalanceEntryType.Adjustment, Amount = payment.Amount,
                            BalanceAfter = old.CurrentBalance, OccurredAt = payment.PaidAt,
                            Notes = $"Collection edit #{payment.Id}: reverse previous amount", CreatedByUserId = actorId
                        });
                    }
                    var balance = db.CustomerBalances.Local.SingleOrDefault(balance => balance.CustomerId == row.PartyId);
                    if (balance is null)
                    {
                        balance = new CustomerBalance { CustomerId = row.PartyId, OpenedAt = rowAt };
                        db.CustomerBalances.Add(balance);
                    }
                    if (payment is null)
                    {
                        payment = new Payment { PaidAt = rowAt, Reference = $"CUSTOMER-PAY-{row.RequestId:N}", CreatedByUserId = actorId };
                        db.Payments.Add(payment);
                    }
                    payment.CustomerBalance = balance;
                    payment.Amount = row.Amount;
                    payment.TenderedAmount = row.Amount;
                    payment.Method = row.Method;
                    payment.Notes = notes;
                    balance.CurrentBalance -= row.Amount;
                    StoreAccess.Money(balance.CurrentBalance, "Customer balance", negative: true);
                    balance.UpdatedAt = DateTimeOffset.UtcNow;
                    balance.UpdatedByUserId = actorId;
                    balance.Entries.Add(new CustomerBalanceEntry
                    {
                        CustomerBalanceId = balance.Id, Type = oldEntity is null ? BalanceEntryType.Payment : BalanceEntryType.Adjustment,
                        Amount = -row.Amount, BalanceAfter = balance.CurrentBalance, OccurredAt = payment.PaidAt,
                        Payment = oldEntity is null ? payment : null,
                        Notes = oldEntity is null ? notes : EditNote("Collection", payment.Id, notes), CreatedByUserId = actorId
                    });
                    await posting.PostAsync(row.Method, row.Amount, CashMovementKind.Sale,
                        $"Customer collection · #{row.PartyId}", payment: payment, cashLocationId: locationId, rowDate: rowDate, occurredAt: rowAt);
                    if (oldEntity is not null)
                    {
                        await ReversePostingsAsync(db, kind, oldEntity.Id, actorId, date, ct);
                    }
                    entity = payment;
                }
                if (oldEntity is not null)
                {
                    entity.UpdatedAt = DateTimeOffset.UtcNow;
                    entity.UpdatedByUserId = actorId;
                    db.RecordChanges.Add(new RecordChange
                    {
                        RecordType = kind, RecordId = entity.Id, Action = "Edit", Reason = "Entry corrected through batch editor",
                        Before = before!, After = Snapshot(entity), CreatedByUserId = actorId
                    });
                }
                entities.Add(entity);
            }
            await db.SaveChangesAsync(ct);
            var ids = entities.Select(entity => entity.Id).ToArray();
            db.FinancialEntryBatches.Add(new FinancialEntryBatch
            {
                RequestId = input.RequestId, Kind = kind, WorkingDate = date,
                ResultJson = JsonSerializer.Serialize(new CachedBatch(payloadHash, ids)), CreatedByUserId = actorId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(ids);
        }, ct);
    }

    private sealed record CachedBatch(string Hash, int[] Ids);

    private static string EditNote(string kind, int id, string notes)
    {
        var text = $"{kind} edit #{id}: apply corrected amount. {notes}";
        return text.Length <= 2000 ? text : text[..2000];
    }

    private static string Snapshot(AuditableEntity entity) => entity switch
    {
        Expense expense => JsonSerializer.Serialize(new { expense.CategoryDefinitionId, expense.StockLocationId, expense.Amount, expense.Method, expense.Notes, expense.ReceiptReference, expense.IsVoided, expense.VoidReason }),
        SupplierPayment payment => JsonSerializer.Serialize(new { SupplierId = payment.SupplierBalance?.SupplierId, payment.Amount, payment.Method, payment.Notes, payment.IsVoided, payment.VoidReason }),
        Payment payment => JsonSerializer.Serialize(new { CustomerId = payment.CustomerBalance?.CustomerId, payment.Amount, payment.Method, payment.Notes, payment.IsVoided, payment.VoidReason }),
        _ => throw new ValidationException("Invalid entry.")
    };

    private static async Task ReversePostingsAsync(AppDbContext db, string kind, int id, int actorId, DateOnly date, CancellationToken ct)
    {
        var cash = await db.CashMovements.Include(move => move.CashSession!.CashDrawer)
            .Where(move => (kind == "expenses" ? move.ExpenseId == id : kind == "supplier-payments" ? move.SupplierPaymentId == id : move.PaymentId == id)
                && move.ReversesMovementId == null && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
            .ToArrayAsync(ct);
        foreach (var move in cash)
        {
            var drawerId = move.CashDrawerId != 0 ? move.CashDrawerId : (move.CashSession?.CashDrawerId ?? 0);
            var drawer = move.CashDrawer
                ?? await db.CashDrawers.SingleOrDefaultAsync(d => d.Id == drawerId, ct)
                ?? await CashDrawerService.RequireDrawerAsync(db, actorId, ct, move.CashSession?.StockLocationId);

            CashDrawerService.Append(db, drawer, new CashMovement
            {
                RequestId = Guid.NewGuid(),
                BusinessDate = move.BusinessDate,
                CreatedAt = move.CreatedAt.AddMilliseconds(1),
                Kind = CashMovementKind.Reversal,
                Amount = -move.Amount,
                Notes = $"Deletion of {kind} #{id}: previous cash posting",
                ReversesMovementId = move.Id,
                CreatedByUserId = actorId
            });

            await CashDrawerService.SyncCountsAndDrawerBalanceAsync(db, drawer, move.CreatedAt, ct);
        }
        var bank = await db.BankMovements.Where(move =>
                (kind == "expenses" ? move.ExpenseId == id : kind == "supplier-payments" ? move.SupplierPaymentId == id : move.PaymentId == id)
                && move.ReversesMovementId == null && !db.BankMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
            .ToArrayAsync(ct);
        foreach (var move in bank)
        {
            await FinancialPosting.BankAsync(db, new BankMovement
            {
                RequestId = Guid.NewGuid(),
                BusinessDate = move.BusinessDate,
                CreatedAt = move.CreatedAt.AddMilliseconds(1),
                Kind = CashMovementKind.Reversal,
                Method = move.Method,
                Amount = -move.Amount,
                Notes = $"Deletion of {kind} #{id}: previous bank posting",
                ReversesMovementId = move.Id,
                CreatedByUserId = actorId
            }, ct);
        }
        if (kind == "customer-payments")
        {
            var clearing = await db.PaymentClearingEntries
                .Where(entry => entry.Id > 0 && entry.PaymentId == id && entry.Status == PaymentClearingStatus.Unsettled)
                .ToArrayAsync(ct);
            foreach (var entry in clearing)
            {
                entry.Status = PaymentClearingStatus.Reversed;
                entry.Notes = string.IsNullOrWhiteSpace(entry.Notes)
                    ? $"Reversal {kind} #{id}: previous clearing posting"
                    : $"{entry.Notes} | Reversal {kind} #{id}: previous clearing posting";
                entry.UpdatedAt = DateTimeOffset.UtcNow;
                entry.UpdatedByUserId = actorId;
            }
        }
    }

    public async Task DeleteAsync(ClaimsPrincipal actor, string kind, int id, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            var requiredRole = Role(kind);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actorId = await StoreAccess.RequireAsync(db, actor, requiredRole, ct);
            var admin = await CashDrawerService.IsAdminAsync(db, actorId, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            await AccountingCalendar.EnsureOpenAsync(db, date, ct);
            var at = AccountingCalendar.At(date, configuration);
            var utcNow = DateTimeOffset.UtcNow;

            if (kind == "supplier-payments")
            {
                var payment = await db.SupplierPayments.Include(p => p.SupplierBalance).Include(p => p.Purchase)
                    .SingleOrDefaultAsync(p => p.Id == id && !p.IsVoided && (p.PurchaseId == null || !p.Purchase!.IsDeleted), ct)
                    ?? throw new ValidationException("Supplier payment not found or already voided.");

                if (payment.PurchaseId is not null)
                {
                    throw new ValidationException("Receipt-linked supplier payments must be voided or corrected through their receiving receipt.");
                }

                if (!admin && payment.CreatedByUserId != actorId)
                {
                    throw new UnauthorizedAccessException("Only the creator or admin can void this payment.");
                }

                var occurred = payment.PaidAt;
                var entryDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(occurred, AccountingCalendar.Zone(configuration)).DateTime);
                if (!admin && entryDate != date)
                {
                    throw new ValidationException("Only entries in the current open working day can be voided. Voiding earlier entries requires an administrator.");
                }
                await AccountingCalendar.EnsureOpenAsync(db, entryDate, ct);

                var before = Snapshot(payment);
                await ReversePostingsAsync(db, kind, payment.Id, actorId, entryDate, ct);

                var balance = payment.SupplierBalance ?? await db.SupplierBalances.SingleOrDefaultAsync(b => b.Id == payment.SupplierBalanceId, ct);
                if (balance != null)
                {
                    balance.CurrentBalance += payment.Amount;
                    StoreAccess.Money(balance.CurrentBalance, "Supplier balance", negative: true);
                    balance.UpdatedAt = utcNow;
                    balance.UpdatedByUserId = actorId;
                    db.SupplierBalanceEntries.Add(new SupplierBalanceEntry
                    {
                        SupplierBalanceId = balance.Id,
                        OccurredAt = payment.PaidAt.AddMilliseconds(1),
                        Type = BalanceEntryType.Adjustment,
                        Amount = payment.Amount,
                        BalanceAfter = balance.CurrentBalance,
                        Notes = $"Void payment #{payment.Id}: {payment.Notes}".Trim(),
                        CreatedByUserId = actorId
                    });
                }

                var voidReason = $"Voided supplier payment #{payment.Id} of {payment.Amount:N2}";
                payment.IsVoided = true;
                payment.VoidedAt = utcNow;
                payment.VoidedByUserId = actorId;
                payment.VoidReason = voidReason;
                payment.UpdatedAt = utcNow;
                payment.UpdatedByUserId = actorId;

                db.RecordChanges.Add(new RecordChange
                {
                    RequestId = Guid.NewGuid(),
                    RecordType = "supplierPayment",
                    RecordId = payment.Id,
                    Action = "Void",
                    Reason = voidReason,
                    Before = before,
                    After = Snapshot(payment),
                    CreatedByUserId = actorId
                });

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            else if (kind == "expenses")
            {
                var expense = await db.Expenses.Include(e => e.CategoryDefinition)
                    .SingleOrDefaultAsync(e => e.Id == id && !e.IsVoided, ct)
                    ?? throw new ValidationException("Expense not found or already voided.");

                if (expense.Amount <= 0 || await db.PaymentPayouts.AnyAsync(p => p.ExpenseId == expense.Id, ct))
                {
                    throw new ValidationException("Clearing payout fee expenses cannot be voided directly.");
                }

                if (!admin && expense.CreatedByUserId != actorId)
                {
                    throw new UnauthorizedAccessException("Only the creator or admin can void this expense.");
                }

                var occurred = expense.OccurredAt;
                var entryDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(occurred, AccountingCalendar.Zone(configuration)).DateTime);
                if (!admin && entryDate != date)
                {
                    throw new ValidationException("Only entries in the current open working day can be voided. Voiding earlier entries requires an administrator.");
                }
                await AccountingCalendar.EnsureOpenAsync(db, entryDate, ct);

                var before = Snapshot(expense);
                await ReversePostingsAsync(db, kind, expense.Id, actorId, entryDate, ct);

                var voidReason = $"Voided expense #{expense.Id} of {expense.Amount:N2}";
                expense.IsVoided = true;
                expense.VoidedAt = utcNow;
                expense.VoidedByUserId = actorId;
                expense.VoidReason = voidReason;
                expense.UpdatedAt = utcNow;
                expense.UpdatedByUserId = actorId;

                db.RecordChanges.Add(new RecordChange
                {
                    RequestId = Guid.NewGuid(),
                    RecordType = "expense",
                    RecordId = expense.Id,
                    Action = "Void",
                    Reason = voidReason,
                    Before = before,
                    After = Snapshot(expense),
                    CreatedByUserId = actorId
                });

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            else if (kind == "customer-payments")
            {
                var payment = await db.Payments.Include(p => p.CustomerBalance)
                    .SingleOrDefaultAsync(p => p.Id == id && p.SaleId == null && !p.IsVoided, ct)
                    ?? throw new ValidationException("Customer collection not found or already voided.");

                if (!admin && payment.CreatedByUserId != actorId)
                {
                    throw new UnauthorizedAccessException("Only the creator or admin can void this collection.");
                }

                var occurred = payment.PaidAt;
                var entryDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(occurred, AccountingCalendar.Zone(configuration)).DateTime);
                if (!admin && entryDate != date)
                {
                    throw new ValidationException("Only entries in the current open working day can be voided. Voiding earlier entries requires an administrator.");
                }
                await AccountingCalendar.EnsureOpenAsync(db, entryDate, ct);

                var before = Snapshot(payment);
                var voidReason = $"Voided customer collection #{payment.Id} of {payment.Amount:N2}";

                // Reverse settled clearing payouts too if this non-cash collection was already cleared
                if (await db.PaymentClearingEntries.AnyAsync(e => e.PaymentId == payment.Id && e.Status == PaymentClearingStatus.Settled, ct))
                {
                    await FinancialPosting.ReverseClearingByPaymentAsync(db, payment.Id, actorId, voidReason, entryDate, configuration, ct);
                }
                await ReversePostingsAsync(db, kind, payment.Id, actorId, entryDate, ct);

                var balance = payment.CustomerBalance ?? await db.CustomerBalances.SingleOrDefaultAsync(b => b.Id == payment.CustomerBalanceId, ct);
                if (balance != null)
                {
                    balance.CurrentBalance += payment.Amount;
                    StoreAccess.Money(balance.CurrentBalance, "Customer balance", negative: true);
                    balance.UpdatedAt = utcNow;
                    balance.UpdatedByUserId = actorId;
                    db.CustomerBalanceEntries.Add(new CustomerBalanceEntry
                    {
                        CustomerBalanceId = balance.Id,
                        OccurredAt = payment.PaidAt.AddMilliseconds(1),
                        Type = BalanceEntryType.Adjustment,
                        Amount = payment.Amount,
                        BalanceAfter = balance.CurrentBalance,
                        Notes = $"Void collection #{payment.Id}: {payment.Notes}".Trim(),
                        CreatedByUserId = actorId
                    });
                }

                payment.IsVoided = true;
                payment.VoidedAt = utcNow;
                payment.VoidedByUserId = actorId;
                payment.VoidReason = voidReason;
                payment.UpdatedAt = utcNow;
                payment.UpdatedByUserId = actorId;

                db.RecordChanges.Add(new RecordChange
                {
                    RequestId = Guid.NewGuid(),
                    RecordType = "payment",
                    RecordId = payment.Id,
                    Action = "Void",
                    Reason = voidReason,
                    Before = before,
                    After = Snapshot(payment),
                    CreatedByUserId = actorId
                });

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            else
            {
                throw new NotSupportedException($"Voiding {kind} is not supported.");
            }
        }, ct);
    }

    private sealed class BatchPosting(AppDbContext db, int actorId, DateOnly defaultDate, CancellationToken ct)
    {
        public async Task PostAsync(PaymentMethod method, decimal amount, CashMovementKind kind, string notes,
            Payment? payment = null, SupplierPayment? supplierPayment = null, Expense? expense = null, int? cashLocationId = null, DateOnly? rowDate = null,
            DateTimeOffset? occurredAt = null)
        {
            var date = rowDate ?? defaultDate;
            await FinancialPosting.PostAsync(db, actorId, method, amount, kind, notes, date, ct,
                payment: payment, supplierPayment: supplierPayment, expense: expense,
                cashLocationId: cashLocationId, cashSession: null, periodValidated: true, occurredAt: occurredAt);
        }
    }
}
