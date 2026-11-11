using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

public sealed partial class MoneyEntryService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    private static string Role(string kind) => kind switch
    {
        "expenses" or "customer-payments" => AppRoles.SalesAssociate,
        "supplier-payments" => AppRoles.InventoryManager,
        _ => throw new ValidationException("Choose expenses, supplier payments or customer payments.")
    };

    public async Task<MoneyEntryPage> ReadAsync(ClaimsPrincipal actor, string kind, DateOnly? date = null, CancellationToken ct = default, int page = 1, int pageSize = 30)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, Role(kind), ct);
        var admin = await CashDrawerService.IsAdminAsync(db, actorId, ct);
        var working = await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
        var selected = date ?? working;
        if (selected == default || selected == DateOnly.MaxValue)
        {
            throw new ValidationException("Choose a valid date.");
        }
        var zone = AccountingCalendar.Zone(configuration);
        DateTimeOffset Utc(DateOnly day) => new(TimeZoneInfo.ConvertTimeToUtc(
            day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
        var from = Utc(selected);
        var through = Utc(selected.AddDays(1));
        var closed = false;
        ExpenseCategoryOption[] categories = kind == "expenses"
            ? await db.ExpenseCategories.AsNoTracking().OrderBy(category => category.Name)
                .Select(category => new ExpenseCategoryOption(category.Id, category.Name, category.IsArchived)).ToArrayAsync(ct)
            : [];
        IReadOnlyList<EntryParty> parties = kind switch
        {
            "supplier-payments" => await db.Suppliers.Where(party => !party.IsDeleted).OrderBy(party => party.Name)
                .Select(party => new EntryParty(party.Id, party.Name, party.Balance == null ? 0 : party.Balance.CurrentBalance)).ToArrayAsync(ct),
            "customer-payments" => await db.Customers.Where(party => !party.IsDeleted).OrderBy(party => party.Name)
                .Select(party => new EntryParty(party.Id, party.Name, party.Balance == null ? 0 : party.Balance.CurrentBalance)).ToArrayAsync(ct),
            _ => []
        };
        var rows = new List<MoneyEntryRow>();
        PageInfo? pagination;
        decimal total;
        int count;
        var cashClosedIds = await db.CashMovements.Where(move => move.BusinessDate == selected && move.CashSession!.ClosedAt != null
                && move.ReversesMovementId == null && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
            .Select(move => kind == "expenses" ? move.ExpenseId : kind == "supplier-payments" ? move.SupplierPaymentId : move.PaymentId)
            .Where(id => id != null).ToArrayAsync(ct);
        MoneyEntryRow Row(AuditableEntity entity, DateTimeOffset at, int partyId, string party,
            int categoryId, string category, decimal amount, PaymentMethod method, string? notes, string? receipt, bool invoice,
            int? locationId = null, string? locationName = null, bool isVoided = false, string? voidReason = null, bool payoutFee = false)
        {
            var reason = isVoided ? "Voided"
                : payoutFee ? "Clearing payout fee"
                : closed ? "Accounting period closed"
                : selected != working ? "Select this working day to edit"
                : invoice ? "Linked receipt payment"
                : !admin && entity.CreatedByUserId != actorId ? "Only the creator or admin can edit"
                : cashClosedIds.Contains(entity.Id) ? "Cash drawer already closed" : null;
            return new(entity.Id, at, partyId, party, categoryId, category, amount, method, notes ?? "", receipt,
                (entity.UpdatedAt ?? entity.CreatedAt).UtcTicks, reason is null, reason)
            {
                StockLocationId = locationId,
                StockLocationName = locationName,
                IsVoided = isVoided,
                VoidReason = voidReason
            };
        }
        if (kind == "expenses")
        {
            var query = db.Expenses.AsNoTracking().Where(expense => expense.OccurredAt >= from && expense.OccurredAt < through
                && (admin || expense.CreatedByUserId == actorId));
            total = await query.Where(expense => !expense.IsVoided).Select(expense => (decimal?)expense.Amount).SumAsync(ct) ?? 0;
            count = await query.Where(expense => !expense.IsVoided).CountAsync(ct);
            var categoryPage = await query.Select(expense => expense.CategoryDefinitionId ?? -(int)expense.Category)
                .Distinct().OrderBy(id => id).ToPageAsync(page, pageSize, ct);
            pagination = categoryPage.Pagination;
            var categoryIds = categoryPage.Items.ToArray();
            var expenses = await query.Where(expense => categoryIds.Contains(expense.CategoryDefinitionId ?? -(int)expense.Category))
                .Include(expense => expense.CategoryDefinition)
                .Include(expense => expense.StockLocation)
                .OrderByDescending(expense => expense.Id).ToArrayAsync(ct);
            var expenseIds = expenses.Select(e => e.Id).ToArray();
            var payoutExpenseIds = (await db.PaymentPayouts.AsNoTracking()
                .Where(p => p.ExpenseId != null && expenseIds.Contains(p.ExpenseId.Value))
                .Select(p => p.ExpenseId!.Value)
                .ToArrayAsync(ct)).ToHashSet();
            foreach (var expense in expenses)
            {
                rows.Add(Row(expense, expense.OccurredAt, 0, "", expense.CategoryDefinitionId ?? -(int)expense.Category,
                    expense.CategoryDefinition?.Name ?? expense.Category.ToString(), expense.Amount, expense.Method,
                    expense.Notes, expense.ReceiptReference, false, expense.StockLocationId, expense.StockLocation?.Name,
                    expense.IsVoided, expense.VoidReason, expense.Amount <= 0 || payoutExpenseIds.Contains(expense.Id)));
            }
        }
        else if (kind == "supplier-payments")
        {
            var query = db.SupplierPayments.AsNoTracking().Where(payment => payment.PaidAt >= from && payment.PaidAt < through
                && (payment.PurchaseId == null || !payment.Purchase!.IsDeleted));
            total = await query.Where(payment => !payment.IsVoided).Select(payment => (decimal?)payment.Amount).SumAsync(ct) ?? 0;
            count = await query.Where(payment => !payment.IsVoided).CountAsync(ct);
            var result = await query.Include(payment => payment.SupplierBalance!.Supplier).Include(payment => payment.Purchase)
                .OrderByDescending(payment => payment.Id).ToPageAsync(page, pageSize, ct);
            pagination = result.Pagination;
            foreach (var payment in result.Items)
            {
                rows.Add(Row(payment, payment.PaidAt, payment.SupplierBalance!.SupplierId, payment.SupplierBalance.Supplier!.Name,
                    0, "", payment.Amount, payment.Method, payment.Notes, null,
                    payment.PurchaseId != null && payment.Purchase!.SupplierId != payment.SupplierBalance.SupplierId,
                    isVoided: payment.IsVoided, voidReason: payment.VoidReason) with
                {
                    PurchaseId = payment.PurchaseId
                });
            }
        }
        else
        {
            var query = db.Payments.AsNoTracking().Where(payment => payment.SaleId == null && payment.CustomerBalanceId != null
                && payment.PaidAt >= from && payment.PaidAt < through);
            total = await query.Where(payment => !payment.IsVoided).Select(payment => (decimal?)payment.Amount).SumAsync(ct) ?? 0;
            count = await query.Where(payment => !payment.IsVoided).CountAsync(ct);
            var result = await query.Include(payment => payment.CustomerBalance!.Customer)
                .OrderByDescending(payment => payment.Id).ToPageAsync(page, pageSize, ct);
            pagination = result.Pagination;
            foreach (var payment in result.Items)
            {
                rows.Add(Row(payment, payment.PaidAt, payment.CustomerBalance!.CustomerId, payment.CustomerBalance.Customer!.Name,
                    0, "", payment.Amount, payment.Method, payment.Notes, null, false,
                    isVoided: payment.IsVoided, voidReason: payment.VoidReason));
            }
        }
        var activeLocations = await db.StockLocations.AsNoTracking().Where(l => !l.IsDeleted).OrderBy(l => l.Name)
            .Select(l => new StockLocationOption(l.Id, l.Name)).ToListAsync(ct);
        var userLocation = await db.Users.Where(u => u.Id == actorId).Select(u => u.CashLocationId).FirstOrDefaultAsync(ct);

        return new(kind, working, selected, selected == working && !closed, admin, total, count, categories, parties, rows)
        {
            CanBackdate = admin,
            Pagination = pagination,
            Locations = activeLocations,
            UserDefaultLocationId = userLocation
        };
    }

    public async Task<PagedResult<ExpenseCategoryOption>> CategoriesPageAsync(ClaimsPrincipal actor,
        int page = 1, int pageSize = 30, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        return await db.ExpenseCategories.AsNoTracking().OrderBy(category => category.Name).ThenBy(category => category.Id)
            .Select(category => new ExpenseCategoryOption(category.Id, category.Name, category.IsArchived))
            .ToPageAsync(page, pageSize, ct);
    }

    public async Task<ExpenseCategoryOption?> CategoryAsync(ClaimsPrincipal actor, int id, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        return await db.ExpenseCategories.AsNoTracking().Where(category => category.Id == id)
            .Select(category => new ExpenseCategoryOption(category.Id, category.Name, category.IsArchived)).SingleOrDefaultAsync(ct);
    }

    public async Task<int> SaveCategoryAsync(ClaimsPrincipal actor, ExpenseCategoryInput input, CancellationToken ct = default)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
        {
            throw new ValidationException("Enter a category name up to 100 characters.");
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);
        if (await db.ExpenseCategories.AnyAsync(category => category.Id != input.Id
            && category.NormalizedName == NameNormalization.Normalize(name), ct))
        {
            throw new ValidationException("A category with that name already exists.");
        }
        var category = input.Id is not null
            ? await db.ExpenseCategories.FindAsync([input.Id.Value], ct) ?? throw new ValidationException("Category not found.")
            : new ExpenseCategoryDefinition
            {
                Name = name,
                NormalizedName = NameNormalization.Normalize(name)
            };
        if (input.Id is null)
        {
            db.ExpenseCategories.Add(category);
        }
        category.Name = name;
        category.NormalizedName = NameNormalization.Normalize(name);
        category.IsArchived = input.IsArchived;
        await db.SaveChangesAsync(ct);
        return category.Id;
    }
}
