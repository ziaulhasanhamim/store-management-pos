using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Services;

public class TradingCalendarServiceTests : PosIntegrationTestBase
{
    [Test]
    public async Task GetCalendarPage_WhenAdmin_ReturnsOk()
    {
        var response = await AdminClient.GetAsync("/administration/calendar");
        var content = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(content).Contains("Trading Calendar &amp; Closed Days");
        await Assert.That(content).Contains("Mark a Non-Trading Day");
    }

    [Test]
    public async Task MarkNonTradingDay_WhenNoOperations_PersistsSuccessfully()
    {
        await using var scope = CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<TradingCalendarService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var testDate = new DateOnly(2026, 11, 6);

        var dto = await calendar.MarkNonTradingDayAsync(AdminUser, new MarkNonTradingDayInput
        {
            Date = testDate,
            Reason = "Weekly Holiday"
        });

        await Assert.That(dto).IsNotNull();
        await Assert.That(dto.Date).IsEqualTo(testDate);
        await Assert.That(dto.Reason).IsEqualTo("Weekly Holiday");

        var lookup = await calendar.GetClosedDatesLookupAsync(testDate, testDate);
        await Assert.That(lookup.Contains(testDate)).IsTrue();

        // AccountingCalendar blocks operations on this day
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            AccountingCalendar.EnsureOpenAsync(db, testDate, CancellationToken.None));
        await Assert.That(ex!.Message).Contains("marked as a closed (non-trading) day");
    }

    [Test]
    public async Task MarkNonTradingDay_WhenActiveSalesExist_ThrowsValidationException()
    {
        await using var scope = CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<TradingCalendarService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var testDate = new DateOnly(2026, 11, 2);
        var testUtc = AccountingCalendar.At(testDate, new TimeOnly(14, 0), null);

        var sale = new Sale
        {
            ReceiptNumber = "TEST-SALE-001",
            StockLocationId = 1,
            CashierId = 1,
            CustomerId = 1,
            TotalAmount = 500m,
            Subtotal = 500m,
            Status = SaleStatus.Completed,
            SoldAt = testUtc,
            IsDeleted = false
        };
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            calendar.MarkNonTradingDayAsync(AdminUser, new MarkNonTradingDayInput
            {
                Date = testDate,
                Reason = "Closed"
            }));

        await Assert.That(ex!.Message).Contains("active sales exist on this date");
    }

    [Test]
    public async Task MarkNonTradingDay_WhenSaleIsDeleted_Succeeds()
    {
        await using var scope = CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<TradingCalendarService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var testDate = new DateOnly(2026, 11, 4);
        var testUtc = AccountingCalendar.At(testDate, new TimeOnly(14, 0), null);

        var sale = new Sale
        {
            ReceiptNumber = "TEST-SALE-DEL",
            StockLocationId = 1,
            CashierId = 1,
            CustomerId = 1,
            TotalAmount = 500m,
            Subtotal = 500m,
            Status = SaleStatus.Completed,
            SoldAt = testUtc,
            IsDeleted = true // Deleted / voided
        };
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        var dto = await calendar.MarkNonTradingDayAsync(AdminUser, new MarkNonTradingDayInput
        {
            Date = testDate,
            Reason = "Clean Day"
        });

        await Assert.That(dto).IsNotNull();
        await Assert.That(dto.Date).IsEqualTo(testDate);
    }

    [Test]
    public async Task MarkNonTradingDay_WhenCashMovementIsReversed_Succeeds()
    {
        await using var scope = CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<TradingCalendarService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var testDate = new DateOnly(2026, 11, 19);
        var testUtc = AccountingCalendar.At(testDate, new TimeOnly(14, 0), null);

        var drawer = await db.CashDrawers.FirstAsync();
        var origMove = new CashMovement
        {
            RequestId = Guid.NewGuid(),
            OccurredAt = testUtc,
            CashDrawerId = drawer.Id,
            Kind = CashMovementKind.CashIn,
            Amount = 100m,
            Notes = "Original cash in",
            CreatedByUserId = 1
        };
        db.CashMovements.Add(origMove);
        await db.SaveChangesAsync();

        var reversalMove = new CashMovement
        {
            RequestId = Guid.NewGuid(),
            OccurredAt = testUtc.AddMinutes(1),
            CashDrawerId = drawer.Id,
            Kind = CashMovementKind.Reversal,
            Amount = -100m,
            ReversesMovementId = origMove.Id,
            Notes = "Reversal of cash in",
            CreatedByUserId = 1
        };
        db.CashMovements.Add(reversalMove);
        await db.SaveChangesAsync();

        var dto = await calendar.MarkNonTradingDayAsync(AdminUser, new MarkNonTradingDayInput
        {
            Date = testDate,
            Reason = "Reversed Day"
        });

        await Assert.That(dto).IsNotNull();
        await Assert.That(dto.Date).IsEqualTo(testDate);
    }

    [Test]
    public async Task SalesTimeline_ExcludesClosedDays_FromPoints()
    {
        await using var scope = CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<TradingCalendarService>();
        var analytics = scope.ServiceProvider.GetRequiredService<AnalyticsService>();

        var from = new DateOnly(2026, 11, 5);
        var closedDate = new DateOnly(2026, 11, 6);
        var through = new DateOnly(2026, 11, 7);

        await calendar.MarkNonTradingDayAsync(AdminUser, new MarkNonTradingDayInput
        {
            Date = closedDate,
            Reason = "Store Closed Test"
        });

        var timeline = await analytics.GetSalesTimelineAsync(AdminUser, from, through);

        await Assert.That(timeline.ClosedDaysExcludedCount).IsEqualTo(1);
        await Assert.That(timeline.ClosedDays!.Contains(closedDate)).IsTrue();
        await Assert.That(timeline.Points.Any(p => p.Date == closedDate)).IsFalse();
        await Assert.That(timeline.Points.Count).IsEqualTo(2); // 5 Nov and 7 Nov only
    }

    [Test]
    public async Task RemoveNonTradingDay_ReopensDay_Successfully()
    {
        await using var scope = CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<TradingCalendarService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var testDate = new DateOnly(2026, 11, 8);
        var dto = await calendar.MarkNonTradingDayAsync(AdminUser, new MarkNonTradingDayInput
        {
            Date = testDate,
            Reason = "Temporary closure"
        });

        await calendar.RemoveNonTradingDayAsync(AdminUser, dto.Id);

        var lookup = await calendar.GetClosedDatesLookupAsync(testDate, testDate);
        await Assert.That(lookup.Contains(testDate)).IsFalse();

        // AccountingCalendar allows operations again
        await AccountingCalendar.EnsureOpenAsync(db, testDate, CancellationToken.None);
    }

    [Test]
    public async Task SaveAsync_BackdatedSupplierPayment_WhenTodayIsClosed_Succeeds()
    {
        await using var scope = CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<TradingCalendarService>();
        var entries = scope.ServiceProvider.GetRequiredService<MoneyEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var supplier = await db.Suppliers.FirstAsync();

        var today = AccountingCalendar.Today(null);
        var yesterday = today.AddDays(-1);

        // Mark today as closed
        var closedDay = await calendar.MarkNonTradingDayAsync(AdminUser, new MarkNonTradingDayInput
        {
            Date = today,
            Reason = "Weekly Holiday"
        });

        try
        {
            // Non-backdated entry on today should fail because today is closed
            var nonBackdated = new MoneyEntryCommand(
                RequestId: Guid.NewGuid(),
                WorkingDate: today,
                Rows: [new MoneyEntryInput
                {
                    RequestId = Guid.NewGuid(),
                    PartyId = supplier.Id,
                    Amount = 100m,
                    Method = PaymentMethod.BankTransfer,
                    Notes = "Non-backdated",
                    TransactionTime = null
                }]);

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                entries.SaveAsync(AdminUser, "supplier-payments", nonBackdated));
            await Assert.That(ex!.Message).Contains("marked as a closed (non-trading) day");

            // Backdated entry to yesterday (which is open) should succeed even though today is closed
            var backdated = new MoneyEntryCommand(
                RequestId: Guid.NewGuid(),
                WorkingDate: today,
                Rows: [new MoneyEntryInput
                {
                    RequestId = Guid.NewGuid(),
                    PartyId = supplier.Id,
                    Amount = 250m,
                    Method = PaymentMethod.BankTransfer,
                    Notes = "Backdated to yesterday",
                    TransactionTime = yesterday.ToDateTime(new TimeOnly(14, 30))
                }]);

            var result = await entries.SaveAsync(AdminUser, "supplier-payments", backdated);
            await Assert.That(result.Ids.Count).IsEqualTo(1);
        }
        finally
        {
            await calendar.RemoveNonTradingDayAsync(AdminUser, closedDay.Id);
        }
    }
}
