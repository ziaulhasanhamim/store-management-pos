using Microsoft.EntityFrameworkCore;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Persistence.Entities;

namespace POSApp.Services;

internal static class DailyFinanceBuilder
{
    public static async Task<DailyFinanceDetails> BuildAsync(
        AppDbContext db, IConfiguration configuration, DateOnly date, CancellationToken ct, int? locationId = null)
    {
        var zone = AccountingCalendar.Zone(configuration);
        DateTimeOffset Utc(DateOnly day) => new(TimeZoneInfo.ConvertTimeToUtc(
            day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
        var from = Utc(date);
        var through = Utc(date.AddDays(1));
        var expenses = await db.Expenses.AsNoTracking()
            .Where(expense => !expense.IsVoided && expense.OccurredAt >= from && expense.OccurredAt < through
                && (locationId == null || expense.StockLocationId == locationId.Value))
            .Select(expense => new
            {
                expense.Category,
                expense.CategoryDefinitionId,
                Name = expense.CategoryDefinition == null ? null : expense.CategoryDefinition.Name,
                expense.Amount
            }).ToArrayAsync(ct);
        var categories = expenses.GroupBy(expense => new
            {
                Id = expense.CategoryDefinitionId ?? -(int)expense.Category,
                Name = expense.Name ?? (expense.Category == ExpenseCategory.Refreshments ? "Snacks / refreshments" : expense.Category.ToString()),
                expense.Category
            })
            .Select(group => new ExpenseCategoryTotal(group.Key.Id, group.Key.Name, group.Sum(expense => expense.Amount), group.Key.Category))
            .OrderBy(category => category.Name).ToArray();

        // Use active ledger postings so corrections and deleted invoices are counted once.
        // Drawer-to-drawer transfers are internal and do not change business funds.
        var cash = await db.CashMovements.AsNoTracking()
            .Where(move => move.OccurredAt >= from && move.OccurredAt < through && move.CashTransferId == null
                && move.ReversesMovementId == null
                && !db.CashMovements.Any(reverse => reverse.ReversesMovementId == move.Id)
                && (locationId == null || (move.CashDrawer != null && move.CashDrawer.StockLocationId == locationId.Value) || (move.CashSession != null && move.CashSession.StockLocationId == locationId.Value)))
            .Select(move => new PaymentSnapshot(
                move.OccurredAt, move.Kind,
                move.Payment != null && move.Payment.SaleId == null,
                move.SupplierPayment != null ? move.SupplierPayment.SupplierBalance!.Supplier!.Name
                    : move.Payment != null ? (move.Payment.CustomerBalance == null ? "Walk-in" : move.Payment.CustomerBalance.Customer!.Name)
                    : move.PayrollEntry != null ? move.PayrollEntry.StaffAccount!.Employee!.Name
                    : move.DebtEntry != null ? move.DebtEntry.LenderBalance!.Lender!.Name : "",
                move.CashDrawer != null && move.CashDrawer.StockLocation != null ? move.CashDrawer.StockLocation.Name : (move.CashSession != null && move.CashSession.StockLocation != null ? move.CashSession.StockLocation.Name : "Cash drawer"),
                PaymentMethod.Cash, move.Amount, move.Notes)).ToArrayAsync(ct);
        var bank = locationId == null
            ? await db.BankMovements.AsNoTracking()
                .Where(move => move.OccurredAt >= from && move.OccurredAt < through && move.Kind != CashMovementKind.OpeningBalance
                    && move.ReversesMovementId == null
                    && !db.BankMovements.Any(reverse => reverse.ReversesMovementId == move.Id))
                .Select(move => new PaymentSnapshot(
                    move.OccurredAt, move.Kind,
                    move.Payment != null && move.Payment.SaleId == null,
                    move.SupplierPayment != null ? move.SupplierPayment.SupplierBalance!.Supplier!.Name
                        : move.Payment != null ? (move.Payment.CustomerBalance == null ? "Walk-in" : move.Payment.CustomerBalance.Customer!.Name)
                        : move.PayrollEntry != null ? move.PayrollEntry.StaffAccount!.Employee!.Name
                        : move.DebtEntry != null ? move.DebtEntry.LenderBalance!.Lender!.Name : "",
                    "Bank / digital", move.Method, move.Amount, move.Notes)).ToArrayAsync(ct)
            : [];

        var directSupplierPayments = await db.SupplierPayments.AsNoTracking()
            .Where(payment => !payment.IsVoided && payment.PaidAt >= from && payment.PaidAt < through
                && (locationId == null || (payment.Purchase != null && payment.Purchase.StockLocationId == locationId.Value))
                && !db.CashMovements.Any(m => m.SupplierPaymentId == payment.Id)
                && !db.BankMovements.Any(m => m.SupplierPaymentId == payment.Id))
            .Select(p => new PaymentSnapshot(
                p.PaidAt, CashMovementKind.SupplierPayment, false,
                p.SupplierBalance!.Supplier!.Name,
                p.Purchase != null && p.Purchase.StockLocation != null ? p.Purchase.StockLocation.Name : "Supplier payment",
                p.Method, -p.Amount, p.Notes ?? ""))
            .ToListAsync(ct);

        // Purchases received on this day (Stock In)
        var purchases = await db.Purchases.AsNoTracking()
            .Where(p => !p.IsDeleted && p.ReceivedAt != null && p.ReceivedAt >= from && p.ReceivedAt < through
                && (locationId == null || p.StockLocationId == locationId.Value))
            .Select(p => new
            {
                p.Id,
                p.PurchaseNumber,
                p.SupplierInvoiceNumber,
                SupplierName = p.Supplier != null ? p.Supplier.Name : "Supplier",
                LocationName = p.StockLocation != null ? p.StockLocation.Name : "Stock location",
                ReceivedAt = p.ReceivedAt!.Value,
                p.TotalAmount,
                p.Notes,
                ItemCount = p.Items.Count,
                TotalUnits = p.Items.Sum(i => i.Quantity)
            })
            .OrderBy(p => p.ReceivedAt)
            .ToListAsync(ct);

        var purchaseSnapshots = purchases.Select(p => new PaymentSnapshot(
            p.ReceivedAt,
            CashMovementKind.SupplierPayment,
            false,
            p.SupplierName,
            p.LocationName,
            PaymentMethod.Cash,
            p.TotalAmount,
            $"Receipt #{p.Id}" + (!string.IsNullOrWhiteSpace(p.PurchaseNumber) && p.PurchaseNumber != p.Id.ToString() ? $" · {p.PurchaseNumber}" : "") + (p.ItemCount > 0 ? $" · {p.ItemCount} items ({p.TotalUnits:0.##} units)" : "") + (string.IsNullOrWhiteSpace(p.Notes) ? "" : $" · {p.Notes}"))
        {
            CustomMethod = "Stock In",
            IsStockIn = true
        }).ToList();

        var stockInList = purchases.Select(p => new DailyStockInItem(
            p.ReceivedAt,
            p.Id,
            p.PurchaseNumber,
            p.SupplierInvoiceNumber,
            p.SupplierName,
            p.LocationName,
            p.ItemCount,
            p.TotalUnits,
            p.TotalAmount,
            p.Notes)).ToList();

        // Completed sales on this day (Stock Out)
        var salesOut = await db.Sales.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Status == SaleStatus.Completed && s.SoldAt >= from && s.SoldAt < through
                && (locationId == null || s.StockLocationId == locationId.Value))
            .Select(s => new
            {
                s.Id,
                s.ReceiptNumber,
                s.SoldAt,
                CustomerName = s.Customer != null ? s.Customer.Name : "Walk-in",
                LocationName = s.StockLocation != null ? s.StockLocation.Name : "Store",
                ItemCount = s.Items.Count,
                TotalUnits = s.Items.Sum(i => i.Quantity),
                TotalCost = s.Items.Sum(i => i.UnitCost * i.Quantity),
                s.TotalAmount
            })
            .OrderBy(s => s.SoldAt)
            .ToListAsync(ct);

        var stockOutList = new List<DailyStockOutItem>();
        foreach (var s in salesOut)
        {
            stockOutList.Add(new DailyStockOutItem(
                s.SoldAt,
                "Sale",
                $"Bill #{s.ReceiptNumber} · {s.CustomerName} ({s.ItemCount} items)",
                s.LocationName,
                s.TotalUnits,
                decimal.Round(s.TotalCost, 2, MidpointRounding.AwayFromZero),
                $"Sale total: {s.TotalAmount:N2}"));
        }

        // Adjustments & Damage movements on this day
        var adjustmentMovements = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.OccurredAt >= from && m.OccurredAt < through
                && (locationId == null || m.StockLocationId == locationId.Value)
                && (m.StockAdjustmentItemId != null || m.StockTakeItemId != null || m.Type == InventoryMovementType.Adjustment)
                && (m.StockAdjustmentItem == null || !m.StockAdjustmentItem.StockAdjustment!.IsVoided)
                && (m.StockTakeItem == null || !m.StockTakeItem.StockTake!.IsVoided))
            .Select(m => new
            {
                m.OccurredAt,
                LocationName = m.StockLocation != null ? m.StockLocation.Name : "Store",
                ProductName = m.Product != null ? m.Product.Name : "Product",
                m.Quantity,
                m.UnitCost,
                m.Notes,
                AdjustmentNotes = m.StockAdjustmentItem != null && m.StockAdjustmentItem.StockAdjustment != null
                    ? m.StockAdjustmentItem.StockAdjustment.Notes : null
            })
            .OrderBy(m => m.OccurredAt)
            .ToListAsync(ct);

        foreach (var m in adjustmentMovements)
        {
            if (m.Quantity < 0)
            {
                var qty = Math.Abs(m.Quantity);
                var cost = Math.Abs(m.Quantity * m.UnitCost);
                stockOutList.Add(new DailyStockOutItem(
                    m.OccurredAt,
                    "Damage / Waste",
                    $"{m.ProductName}" + (!string.IsNullOrWhiteSpace(m.AdjustmentNotes ?? m.Notes) ? $" · {m.AdjustmentNotes ?? m.Notes}" : ""),
                    m.LocationName,
                    qty,
                    cost,
                    m.Notes));
            }
            else if (m.Quantity > 0)
            {
                stockInList.Add(new DailyStockInItem(
                    m.OccurredAt,
                    0,
                    "Adjustment (+)",
                    null,
                    "Inventory Correction",
                    m.LocationName,
                    1,
                    m.Quantity,
                    m.Quantity * m.UnitCost,
                    m.AdjustmentNotes ?? m.Notes));
            }
        }

        // Stock Transfers on this day
        var transfers = await db.StockTransfers.AsNoTracking()
            .Where(t => !t.IsVoided && t.OccurredAt >= from && t.OccurredAt < through
                && (locationId == null || t.SourceLocationId == locationId.Value || t.DestinationLocationId == locationId.Value))
            .Select(t => new
            {
                t.Id,
                t.OccurredAt,
                t.SourceLocationId,
                SourceName = t.SourceLocation != null ? t.SourceLocation.Name : "Source",
                t.DestinationLocationId,
                DestinationName = t.DestinationLocation != null ? t.DestinationLocation.Name : "Destination",
                t.Notes,
                TotalUnits = t.Items.Sum(i => i.Quantity),
                ItemCount = t.Items.Count
            })
            .OrderBy(t => t.OccurredAt)
            .ToListAsync(ct);

        foreach (var t in transfers)
        {
            if (locationId == null)
            {
                stockOutList.Add(new DailyStockOutItem(
                    t.OccurredAt,
                    "Transfer",
                    $"{t.SourceName} \u2192 {t.DestinationName} ({t.ItemCount} items)",
                    t.SourceName,
                    t.TotalUnits,
                    0,
                    t.Notes));
            }
            else
            {
                if (t.SourceLocationId == locationId.Value)
                {
                    stockOutList.Add(new DailyStockOutItem(
                        t.OccurredAt,
                        "Transfer Out",
                        $"Transfer to {t.DestinationName} ({t.ItemCount} items)",
                        t.SourceName,
                        t.TotalUnits,
                        0,
                        t.Notes));
                }
                if (t.DestinationLocationId == locationId.Value)
                {
                    stockInList.Add(new DailyStockInItem(
                        t.OccurredAt,
                        0,
                        $"Transfer #{t.Id}",
                        null,
                        $"Transfer from {t.SourceName}",
                        t.DestinationName,
                        t.ItemCount,
                        t.TotalUnits,
                        0,
                        t.Notes));
                }
            }
        }

        var totalStockInVal = stockInList.Sum(x => x.TotalAmount);
        var totalStockInQty = stockInList.Sum(x => x.TotalQuantity);
        var totalStockOutVal = stockOutList.Sum(x => x.TotalCostValue);
        var totalStockOutQty = stockOutList.Sum(x => x.TotalQuantity);

        var stockActivity = new DailyStockActivity(
            totalStockInVal,
            totalStockInQty,
            totalStockOutVal,
            totalStockOutQty,
            totalStockInVal - totalStockOutVal,
            totalStockInQty - totalStockOutQty,
            stockInList.OrderBy(x => x.At).ToArray(),
            stockOutList.OrderBy(x => x.At).ToArray());

        var clearingEntries = await db.PaymentClearingEntries.AsNoTracking()
            .Where(e => e.Status == PaymentClearingStatus.Unsettled && (locationId == null || e.StockLocationId == locationId.Value)
                && (e.Payment == null || (!e.Payment.IsVoided && (e.Payment.SaleId == null || (!e.Payment.Sale!.IsDeleted && e.Payment.Sale.Status != SaleStatus.Voided)))))
            .Select(e => new { e.Method, e.GrossAmount, e.BusinessDate })
            .ToListAsync(ct);
        var awaitingSummary = new AwaitingPayoutSummary(
            clearingEntries.Where(e => e.Method == PaymentMethod.Card).Sum(e => e.GrossAmount),
            clearingEntries.Where(e => e.Method == PaymentMethod.MobileWallet).Sum(e => e.GrossAmount),
            clearingEntries.Count,
            clearingEntries.Count > 0 ? clearingEntries.Min(e => e.BusinessDate) : null);

        var allPayments = cash.Concat(bank).Concat(directSupplierPayments).Concat(purchaseSnapshots)
            .OrderBy(row => row.At)
            .Select(row => new DayPayment(row.At, Label(row.Kind, row.IsCollection), row.Party,
                row.Account, row.Method, row.Amount, row.Notes)
            {
                CustomMethod = row.CustomMethod,
                IsStockIn = row.IsStockIn
            }).ToArray();

        return new DailyFinanceDetails(categories, allPayments)
        {
                        AwaitingPayout = awaitingSummary,
            StockActivity = stockActivity
        };
    }

    private static string Label(CashMovementKind kind, bool collection) => collection ? "Customer collections" : kind switch
    {
        CashMovementKind.Sale => "Sales receipts / refunds",
        CashMovementKind.SupplierPayment => "Supplier payments",
        CashMovementKind.Expense => "Expenses",
        CashMovementKind.Payroll => "Salary / bonus / advances",
        CashMovementKind.OwnerContribution => "Owner contributions",
        CashMovementKind.OwnerWithdrawal => "Owner withdrawals",
        CashMovementKind.Transfer => "Account transfers",
        CashMovementKind.DebtBorrowing => "Money borrowed",
        CashMovementKind.DebtRepayment => "Debt repayments",
        _ => "Other cash movements"
    };

    private sealed record PaymentSnapshot(DateTimeOffset At, CashMovementKind Kind, bool IsCollection,
        string Party, string Account, PaymentMethod Method, decimal Amount, string Notes)
    {
        public string? CustomMethod { get; init; }
        public bool IsStockIn { get; init; }
    }
}
