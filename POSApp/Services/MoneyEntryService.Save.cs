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
            var date = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
            AccountingCalendar.ValidateExpected(date, WorkingDayRequest.Date);
            if (date != input.WorkingDate)
            {
                throw new ValidationException("The working day has changed. Reload before saving these entries.");
            }
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
                if (row.Id is { } id)
                {
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
                int? locationId = null;
                if (oldEntity is not null)
                {
                    locationId = await db.CashMovements.Where(move =>
                            (kind == "expenses" ? move.ExpenseId == oldEntity.Id : kind == "supplier-payments" ? move.SupplierPaymentId == oldEntity.Id : move.PaymentId == oldEntity.Id)
                            && move.ReversesMovementId == null && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
                        .Select(move => move.CashSession!.StockLocationId).FirstOrDefaultAsync(ct);
                    if (kind != "customer-payments")
                    {
                        await ReversePostingsAsync(db, kind, oldEntity.Id, actorId, date, ct);
                    }
                }
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
                        expense = new Expense { RequestId = row.RequestId, OccurredAt = at, Notes = notes, CreatedByUserId = actorId };
                        db.Expenses.Add(expense);
                    }
                    expense.CategoryDefinitionId = category.Id;
                    expense.Category = category.BuiltInCategory ?? ExpenseCategory.Other;
                    expense.Amount = row.Amount;
                    expense.Method = row.Method;
                    expense.Notes = notes;
                    expense.ReceiptReference = row.Receipt?.Trim();
                    await posting.PostAsync(row.Method, -row.Amount, CashMovementKind.Expense,
                        notes.Length == 0 ? category.Name : notes, expense: expense, cashLocationId: locationId);
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
                        balance = new SupplierBalance { SupplierId = row.PartyId, OpenedAt = at };
                        db.SupplierBalances.Add(balance);
                    }
                    if (payment is null)
                    {
                        payment = new SupplierPayment
                        {
                            SupplierBalanceId = balance.Id, Amount = row.Amount, PaidAt = at,
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
                        $"Supplier payment · #{row.PartyId}", supplierPayment: payment, cashLocationId: locationId);
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
                        balance = new CustomerBalance { CustomerId = row.PartyId, OpenedAt = at };
                        db.CustomerBalances.Add(balance);
                    }
                    if (payment is null)
                    {
                        payment = new Payment { PaidAt = at, Reference = $"CUSTOMER-PAY-{row.RequestId:N}", CreatedByUserId = actorId };
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
                        $"Customer collection · #{row.PartyId}", payment: payment, cashLocationId: locationId);
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
        Expense expense => JsonSerializer.Serialize(new { expense.CategoryDefinitionId, expense.Amount, expense.Method, expense.Notes, expense.ReceiptReference }),
        SupplierPayment payment => JsonSerializer.Serialize(new { SupplierId = payment.SupplierBalance?.SupplierId, payment.Amount, payment.Method, payment.Notes }),
        Payment payment => JsonSerializer.Serialize(new { CustomerId = payment.CustomerBalance?.CustomerId, payment.Amount, payment.Method, payment.Notes }),
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
            if (move.CashSession!.ClosedAt != null || move.BusinessDate != date)
            {
                throw new ValidationException("This entry belongs to a closed cash drawer or another working day.");
            }
            CashDrawerService.Append(db, move.CashSession, new CashMovement
            {
                RequestId = Guid.NewGuid(), BusinessDate = move.BusinessDate, Kind = CashMovementKind.Reversal,
                Amount = -move.Amount, Notes = $"Edit {kind} #{id}: reverse previous cash posting",
                ReversesMovementId = move.Id, CreatedByUserId = actorId
            });
        }
        var bank = await db.BankMovements.Where(move =>
                (kind == "expenses" ? move.ExpenseId == id : kind == "supplier-payments" ? move.SupplierPaymentId == id : move.PaymentId == id)
                && move.ReversesMovementId == null && !db.BankMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
            .ToArrayAsync(ct);
        foreach (var move in bank)
        {
            if (move.BusinessDate != date)
            {
                throw new ValidationException("This posting belongs to another working day.");
            }
            await FinancialPosting.BankAsync(db, new BankMovement
            {
                RequestId = Guid.NewGuid(), BusinessDate = move.BusinessDate, Kind = CashMovementKind.Reversal,
                Method = move.Method, Amount = -move.Amount, Notes = $"Edit {kind} #{id}: reverse previous bank posting",
                ReversesMovementId = move.Id, CreatedByUserId = actorId
            }, ct);
        }
    }

    private sealed class BatchPosting(AppDbContext db, int actorId, DateOnly date, CancellationToken ct)
    {
        private readonly Dictionary<int, CashSession> sessions = [];

        public async Task PostAsync(PaymentMethod method, decimal amount, CashMovementKind kind, string notes,
            Payment? payment = null, SupplierPayment? supplierPayment = null, Expense? expense = null, int? cashLocationId = null)
        {
            CashSession? session = null;
            if (method == PaymentMethod.Cash)
            {
                var key = cashLocationId ?? 0;
                if (!sessions.TryGetValue(key, out session))
                {
                    session = await CashDrawerService.RequireOpenAsync(db, actorId, ct, cashLocationId);
                    sessions[key] = session;
                    if (session.StockLocationId is { } location)
                    {
                        sessions[location] = session;
                    }
                }
            }
            await FinancialPosting.PostAsync(db, actorId, method, amount, kind, notes, date, ct,
                payment: payment, supplierPayment: supplierPayment, expense: expense,
                cashSession: session, periodValidated: true);
        }
    }
}
