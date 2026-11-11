using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Persistence;
using POSApp.Persistence.Entities;
using POSApp.Services;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Services;

public class CashDrawerAndFinanceServiceTests : PosIntegrationTestBase
{
    [Test]
    public async Task CashDrawer_OpenAdjustAndClose_MaintainsAccurateSessionAndMovements()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Create a new location to test drawer full lifecycle from scratch
        var locId = await inventory.AddLocationAsync(AdminUser, "North Outlet", canSell: true);

        // 1. Open drawer with 500 opening cash
        var openInput = new OpenCashInput
        {
            RequestId = Guid.NewGuid(),
            StockLocationId = locId,
            OpeningCash = 500m
        };
        var sessionId = await drawerService.OpenAsync(AdminUser, openInput);
        await sessionId.Should().BeGreaterThan(0);

        var sessionInDb = await db.CashSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId);
        await sessionInDb.Should().NotBeNull();
        await sessionInDb!.OpeningCash.Should().BeEqualTo(500m);
        await sessionInDb.ExpectedCash.Should().BeEqualTo(500m);

        // 2. Adjust cash: add 200 petty cash deposit
        var adjustInput = new CashAdjustmentInput
        {
            RequestId = Guid.NewGuid(),
            SessionId = sessionId,
            Payout = false,
            Amount = 200m,
            Notes = "Midday cash addition"
        };
        await drawerService.AdjustAsync(AdminUser, adjustInput);

        // Check via ReadAsync
        var page = await drawerService.ReadAsync(AdminUser, sessionId);
        await page.Current.Should().NotBeNull();
        await page.Current!.Expected.Should().BeEqualTo(700m);

        // 3. Close drawer with actual cash 700
        var closeInput = new CloseCashInput
        {
            SessionId = sessionId,
            ExpectedCash = 700m,
            ActualCash = 700m,
            Confirmed = true,
            Notes = "Clean close"
        };
        await drawerService.CloseAsync(AdminUser, closeInput);

        var closedSession = await db.CashSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        await closedSession.ClosedAt.HasValue.Should().BeTrue();
        await closedSession.ActualClosingCash.Should().BeEqualTo(700m);
        await closedSession.Difference.Should().BeEqualTo(0m);

        // 4. Verify ReadAsync reflects "Counted and closed", NOT "Open" (SessionId must be null!)
        var pageAfterClose = await drawerService.ReadAsync(AdminUser);
        var locRow = pageAfterClose.Locations.Single(l => l.Id == locId);
        await locRow.SessionId.Should().BeNull();
        await locRow.ClosedToday.Should().BeTrue();
        await locRow.ClosedSessionId.Should().BeEqualTo(sessionId);
    }

    [Test]
    public async Task ClosingService_DailyClosing_ComputesAccurateRevenueFromRealSales()
    {
        var today = AccountingCalendar.Today(null);

        // 1. Setup product & stock
        int productId;
        await using (var scope = CreateScope())
        {
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
            productId = await inventory.SaveProductAsync(InventoryUser, null, new ProductInput
            {
                Name = "Wireless Bluetooth Earbuds",
                Unit = "piece",
                CostPrice = 30m,
                SellingPrice = 75m,
                SupplierId = 1,
                CategoryId = 1
            });

            await inventory.ReceiveAsync(InventoryUser, new ReceiveCommand(
                RequestId: Guid.NewGuid(),
                SupplierId: 1,
                LocationId: 1,
                Lines: [new ReceiveLine(productId, Quantity: 10m, UnitCost: 30m)],
                Payment: 300m,
                Method: PaymentMethod.Cash,
                Invoice: "PO-EARBUDS-001",
                Notes: "Initial batch"));
        }

        // 2. Complete sale for 2 units @ 75 = 150
        await using (var scope = CreateScope())
        {
            var sales = scope.ServiceProvider.GetRequiredService<SalesService>();
            await sales.SaveAsync(SalesUser, new SalesCommand(
                RequestId: Guid.NewGuid(),
                LocationId: 1,
                CustomerId: 1,
                Lines: [new SalesLine(productId, Quantity: 2m, UnitPrice: 75m)],
                Discount: 0m,
                Payment: 150m,
                Method: PaymentMethod.Cash,
                Notes: "Closing calculation test sale"), complete: true);
        }

        // 3. Read daily closing totals
        await using (var scope = CreateScope())
        {
            var closingService = scope.ServiceProvider.GetRequiredService<ClosingService>();
            var totals = await closingService.ReadAsync(AdminUser, today, ClosingKind.Day);

            await totals.Should().NotBeNull();
            await totals.Revenue.Should().BeEqualTo(150m);
            await totals.SalesReceipts.Should().BeEqualTo(150m);
            await totals.Cogs.Should().BeEqualTo(60m); // 2 units @ 30 cost
        }
    }

    [Test]
    public async Task BalanceIntegrityService_WhenConsistent_ReportsZeroIssues()
    {
        await using var scope = CreateScope();
        var integrity = scope.ServiceProvider.GetRequiredService<BalanceIntegrityService>();

        var issues = await integrity.ReadAsync(AdminUser);
        await issues.Should().NotBeNull();
        await issues.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task CashDrawer_OpenPreseededDrawer_SetsOpeningBalanceAndExpectedCashCorrectly()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Create a location and pre-seed a cash drawer with 0 balance and null OpeningDate (like AdminSeeder)
        var locId = await inventory.AddLocationAsync(AdminUser, "East Outlet", canSell: true);
        var preseededDrawer = new CashDrawer
        {
            StockLocationId = locId,
            OpeningBalance = 0m,
            CurrentCash = 0m,
            OpeningDate = null
        };
        db.CashDrawers.Add(preseededDrawer);
        await db.SaveChangesAsync();

        // 2. Open drawer with 8300 opening cash
        var openInput = new OpenCashInput
        {
            RequestId = Guid.NewGuid(),
            StockLocationId = locId,
            OpeningCash = 8300m
        };
        var sessionId = await drawerService.OpenAsync(AdminUser, openInput);
        await sessionId.Should().BeGreaterThan(0);

        // 3. Verify drawer and session reflect 8300
        var sessionInDb = await db.CashSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        await sessionInDb.OpeningCash.Should().BeEqualTo(8300m);
        await sessionInDb.ExpectedCash.Should().BeEqualTo(8300m);

        var drawerInDb = await db.CashDrawers.AsNoTracking().SingleAsync(d => d.StockLocationId == locId);
        await drawerInDb.OpeningBalance.Should().BeEqualTo(8300m);
        await drawerInDb.CurrentCash.Should().BeEqualTo(8300m);
        await drawerInDb.OpeningDate.HasValue.Should().BeTrue();
    }

    [Test]
    public async Task CashDrawer_Open_CreatesActiveShiftSession()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var locId = await inventory.AddLocationAsync(AdminUser, "West Outlet", canSell: true);

        var openInput = new OpenCashInput
        {
            RequestId = Guid.NewGuid(),
            StockLocationId = locId,
            OpeningCash = 1000m
        };

        await drawerService.OpenAsync(AdminUser, openInput);

        var drawer = await db.CashDrawers.Include(d => d.CurrentSession).SingleAsync(d => d.StockLocationId == locId);
        await drawer.CurrentSession.Should().NotBeNull();
        await drawer.CurrentSession!.OpeningCash.Should().BeEqualTo(1000m);
        await drawer.CurrentSession.ClosedAt.Should().BeNull();
    }

    [Test]
    public async Task BankService_AdjustmentAndMonthOpening_UpdatesBalanceAccurately()
    {
        await using var scope = CreateScope();
        var bankService = scope.ServiceProvider.GetRequiredService<BankService>();

        var page = await bankService.ReadAsync(AdminUser);
        await page.Should().NotBeNull();
        await page.MonthDate.HasValue.Should().BeTrue();
        var initialBalance = page.Balance;

        // Perform bank balance adjustment (non-drawer reconciliation)
        var adjustInput = new BankInput
        {
            RequestId = Guid.NewGuid(),
            Action = "adjustment",
            Amount = 250m,
            Notes = "Monthly bank interest earned",
            Date = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime)
        };
        await bankService.SaveAsync(AdminUser, adjustInput);

        var updated = await bankService.ReadAsync(AdminUser);
        await updated.Balance.Should().BeEqualTo(initialBalance + 250m);
    }

    [Test]
    public async Task MoneyEntryService_DeleteAsync_VoidSupplierPayment_ReversesMovementsAndRestoresBalance()
    {
        int paymentId;
        int supplierId;
        decimal balanceBeforePayment;

        await using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplier = await db.Suppliers.Include(s => s.Balance).FirstAsync();
            supplierId = supplier.Id;
            balanceBeforePayment = supplier.Balance?.CurrentBalance ?? 0m;

            var entries = scope.ServiceProvider.GetRequiredService<MoneyEntryService>();
            var calendar = scope.ServiceProvider.GetRequiredService<ClosingService>();
            var today = await calendar.SelectedAsync(AdminUser);

            // Record a supplier payment of 300 via Bank
            var command = new MoneyEntryCommand(
                RequestId: Guid.NewGuid(),
                WorkingDate: today,
                Rows: [new MoneyEntryInput
                {
                    RequestId = Guid.NewGuid(),
                    Id = null,
                    PartyId = supplierId,
                    CategoryId = 0,
                    Amount = 300m,
                    Method = PaymentMethod.BankTransfer,
                    Notes = "Test payment to be voided",
                    Receipt = null
                }]);

            var saveResult = await entries.SaveAsync(AdminUser, "supplier-payments", command);
            paymentId = saveResult.Ids.Single();
        }

        // Verify balance decreased after payment
        await using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var balanceAfterPayment = (await db.SupplierBalances.SingleAsync(b => b.SupplierId == supplierId)).CurrentBalance;
            await balanceAfterPayment.Should().BeEqualTo(balanceBeforePayment - 300m);

            // Void the payment
            var entries = scope.ServiceProvider.GetRequiredService<MoneyEntryService>();
            await entries.DeleteAsync(AdminUser, "supplier-payments", paymentId);
        }

        // Verify supplier balance is restored and payment is deleted
        await using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var restoredBalance = (await db.SupplierBalances.SingleAsync(b => b.SupplierId == supplierId)).CurrentBalance;
            await restoredBalance.Should().BeEqualTo(balanceBeforePayment);

            var paymentInDb = await db.SupplierPayments.SingleOrDefaultAsync(p => p.Id == paymentId);
            await paymentInDb.Should().NotBeNull();
            await paymentInDb!.IsVoided.Should().BeTrue();
            await paymentInDb.VoidedAt.HasValue.Should().BeTrue();

            // Verify audit entry exists
            var auditEntry = await db.SupplierBalanceEntries
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync(e => e.SupplierBalance!.SupplierId == supplierId && e.Type == BalanceEntryType.Adjustment);
            await auditEntry.Should().NotBeNull();
            await auditEntry!.Amount.Should().BeEqualTo(300m);
        }
    }

    [Test]
    public async Task ChangeDateAsync_WhenAdminBackdatesExpense_UpdatesTimestampAndAuditLog()
    {
        var today = AccountingCalendar.Today(null);
        int expenseId;

        await using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cat = new ExpenseCategoryDefinition
            {
                Name = "Office Utilities",
                NormalizedName = "OFFICE UTILITIES",
                IsArchived = false
            };
            db.ExpenseCategories.Add(cat);
            await db.SaveChangesAsync();

            var entries = scope.ServiceProvider.GetRequiredService<MoneyEntryService>();
            var command = new MoneyEntryCommand(
                RequestId: Guid.NewGuid(),
                WorkingDate: today,
                Rows: [new MoneyEntryInput
                {
                    RequestId = Guid.NewGuid(),
                    Id = null,
                    PartyId = 0,
                    CategoryId = cat.Id,
                    Amount = 50m,
                    Method = PaymentMethod.Cash,
                    Notes = "Office supplies",
                    Receipt = null
                }]);
            var result = await entries.SaveAsync(AdminUser, "expenses", command);
            expenseId = result.Ids[0];
        }

        // Now backdate the expense using DateCorrectionService
        var pastDate = today.AddDays(-3);
        var pastTimestamp = new DateTimeOffset(pastDate.Year, pastDate.Month, pastDate.Day, 14, 0, 0, TimeSpan.Zero);

        await using (var scope = CreateScope())
        {
            var corrections = scope.ServiceProvider.GetRequiredService<DateCorrectionService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await corrections.ChangeDateAsync(AdminUser, "expense", expenseId, pastTimestamp, "Late entry submitted by clerk");

            var expense = await db.Expenses.SingleAsync(e => e.Id == expenseId);
            await expense.OccurredAt.Should().BeEqualTo(pastTimestamp);

            var movement = await db.CashMovements.SingleAsync(m => m.ExpenseId == expenseId);
            await movement.BusinessDate.Should().BeEqualTo(pastDate);

            var audit = await db.RecordChanges.FirstOrDefaultAsync(r => r.RecordType == "expense" && r.RecordId == expenseId);
            await audit.Should().NotBeNull();
            await audit!.Reason.Should().BeEqualTo("Late entry submitted by clerk");
        }
    }

    [Test]
    public async Task PhysicalCashCount_Balanced_CompletesImmediately_AndSetsCheckpoint()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var drawer = await db.CashDrawers.FirstAsync(d => d.StockLocationId == 1);
        var expected = drawer.CurrentCash;

        var countId = await drawerService.SubmitCountAsync(AdminUser, new CashCountInput
        {
            LocationId = drawer.StockLocationId ?? 1,
            CountedCash = expected,
            Notes = "Perfect balance morning count"
        });

        var count = await db.CashCounts.SingleAsync(c => c.Id == countId);
        await count.Status.Should().BeEqualTo(CashCountStatus.Completed);
        await count.CompletedAt.HasValue.Should().BeTrue();
        await count.VarianceAcceptedAt.HasValue.Should().BeFalse();

        var updatedDrawer = await db.CashDrawers.AsNoTracking().SingleAsync(d => d.Id == drawer.Id);
        await updatedDrawer.LastCountedCash.Should().BeEqualTo(expected);
        await updatedDrawer.LastCountedAt.HasValue.Should().BeTrue();
    }

    [Test]
    public async Task PhysicalCashCount_WithVariance_QueuesAsPending_AndAdminAcceptsVariance()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var drawer = await db.CashDrawers.FirstAsync(d => d.StockLocationId == 1);
        var originalCash = drawer.CurrentCash;
        var countedCash = originalCash - 150m;

        // 1. Submit count with difference != 0
        var countId = await drawerService.SubmitCountAsync(SalesUser, new CashCountInput
        {
            LocationId = drawer.StockLocationId ?? 1,
            CountedCash = countedCash,
            Notes = "Cash shortage counted at shift handover"
        });

        var count = await db.CashCounts.SingleAsync(c => c.Id == countId);
        await count.Status.Should().BeEqualTo(CashCountStatus.PendingReview);

        // Drawer balance should NOT be altered yet
        var unchangedDrawer = await db.CashDrawers.AsNoTracking().SingleAsync(d => d.Id == drawer.Id);
        await unchangedDrawer.CurrentCash.Should().BeEqualTo(originalCash);

        // 2. Admin accepts variance
        await drawerService.AcceptVarianceAsync(AdminUser, new AcceptVarianceInput
        {
            CountId = countId,
            Confirmed = true,
            Notes = "Approved shortage by manager"
        });

        var reviewedCount = await db.CashCounts.AsNoTracking().Include(c => c.VarianceMovement).SingleAsync(c => c.Id == countId);
        await reviewedCount.Status.Should().BeEqualTo(CashCountStatus.VarianceAccepted);
        await reviewedCount.AcceptedVariance.Should().BeEqualTo(-150m);
        await reviewedCount.VarianceMovementId.HasValue.Should().BeTrue();
        await reviewedCount.VarianceMovement!.Amount.Should().BeEqualTo(-150m);
        await reviewedCount.VarianceMovement.Kind.Should().BeEqualTo(CashMovementKind.Variance);
        await reviewedCount.VarianceMovement.Notes.Should().Contain("missing BDT 150.00");

        // Drawer balance now matches the physical count anchor
        var finalDrawer = await db.CashDrawers.AsNoTracking().SingleAsync(d => d.Id == drawer.Id);
        await finalDrawer.CurrentCash.Should().BeEqualTo(countedCash);
        await finalDrawer.LastCountedCash.Should().BeEqualTo(countedCash);
    }

    [Test]
    public async Task PhysicalCashCount_BackdatedSaleBeforeAnchor_DoesNotAlterDrawerBalance_AndDetectsDrift()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var drawer = await db.CashDrawers.FirstAsync(d => d.StockLocationId == 1);
        var anchorTime = DateTimeOffset.UtcNow.AddHours(-1);

        // 1. Submit and accept an anchor count
        var countId = await drawerService.SubmitCountAsync(AdminUser, new CashCountInput
        {
            LocationId = drawer.StockLocationId ?? 1,
            CountedCash = 5000m,
            CountedAt = anchorTime,
            IsBackdate = true,
            Notes = "Anchor physical count at 5000"
        });

        // 2. Accept variance to set the anchor
        await drawerService.AcceptVarianceAsync(AdminUser, new AcceptVarianceInput
        {
            CountId = countId,
            Confirmed = true,
            Notes = "Manager confirmed anchor"
        });

        var drawerAfterAnchor = await db.CashDrawers.AsNoTracking().SingleAsync(d => d.Id == drawer.Id);
        await drawerAfterAnchor.CurrentCash.Should().BeEqualTo(5000m);

        // 3. Backfill a sale movement dated BEFORE the anchor
        var backfillTime = anchorTime.AddMinutes(-10);
        var backfilledMove = new CashMovement
        {
            RequestId = Guid.NewGuid(),
            BusinessDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(backfillTime, AccountingCalendar.Zone(null)).DateTime),
            Kind = CashMovementKind.Sale,
            Amount = 300m,
            Notes = "Backfilled historical cash sale",
            CreatedAt = backfillTime,
            CreatedByUserId = 1
        };
        await db.Entry(drawer).ReloadAsync();
        CashDrawerService.Append(db, drawer, backfilledMove);
        await db.SaveChangesAsync();

        // 4. Current drawer balance MUST remain 5000 (unaltered by historical backfilling)
        var drawerAfterBackfill = await db.CashDrawers.AsNoTracking().SingleAsync(d => d.Id == drawer.Id);
        await drawerAfterBackfill.CurrentCash.Should().BeEqualTo(5000m);

        // 5. ReadAsync should show the variance drift on the historical count
        var page = await drawerService.ReadAsync(AdminUser);
        var countRow = page.CountHistory.First(c => c.Id == countId);
        await countRow.HasVarianceDrift.Should().BeTrue();
    }

    [Test]
    public async Task PhysicalCashCount_BackdatedBeforeLastAccepted_ThrowsValidationException()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var drawer = await db.CashDrawers.FirstAsync(d => d.StockLocationId == 1);
        var t1 = DateTimeOffset.UtcNow.AddHours(-2);

        // Accept count at t1
        var countId = await drawerService.SubmitCountAsync(AdminUser, new CashCountInput
        {
            LocationId = drawer.StockLocationId ?? 1,
            CountedCash = drawer.CurrentCash,
            CountedAt = t1,
            IsBackdate = true,
            Notes = "Baseline count"
        });

        // Try to backdate earlier than t1
        var ex = await Assert.ThrowsAsync<ValidationException>(() => drawerService.SubmitCountAsync(AdminUser, new CashCountInput
        {
            LocationId = drawer.StockLocationId ?? 1,
            CountedCash = 1000m,
            CountedAt = t1.AddMinutes(-30),
            IsBackdate = true,
            Notes = "Impossible earlier count"
        }));

        await ex!.Message.Should().Contain("cannot be on or earlier than the last accepted count");
    }

    [Test]
    public async Task PhysicalCashCount_ZeroVariance_AutoAcceptedAsAnchor()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var drawer = await db.CashDrawers.FirstAsync(d => d.StockLocationId == 1);

        var countId = await drawerService.SubmitCountAsync(SalesUser, new CashCountInput
        {
            LocationId = drawer.StockLocationId ?? 1,
            CountedCash = drawer.CurrentCash,
            Notes = "Matched physical count"
        });

        var count = await db.CashCounts.AsNoTracking().SingleAsync(c => c.Id == countId);
        await count.Status.Should().BeEqualTo(CashCountStatus.Completed);
        await count.AcceptedVariance.Should().BeEqualTo(0m);
        await count.CompletedAt.HasValue.Should().BeTrue();
    }

    [Test]
    public async Task BulkChangeDateAsync_UpdatesMultipleRecords_WithAuditLog()
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entries = scope.ServiceProvider.GetRequiredService<MoneyEntryService>();
        var corrections = scope.ServiceProvider.GetRequiredService<DateCorrectionService>();

        var cat = new ExpenseCategoryDefinition { Name = "Bulk Utility", NormalizedName = "BULK UTILITY" };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();

        var today = AccountingCalendar.Today(null);
        var res1 = await entries.SaveAsync(AdminUser, "expenses", new MoneyEntryCommand(Guid.NewGuid(), today,
            [new MoneyEntryInput { RequestId = Guid.NewGuid(), CategoryId = cat.Id, Amount = 10m }]));
        var res2 = await entries.SaveAsync(AdminUser, "expenses", new MoneyEntryCommand(Guid.NewGuid(), today,
            [new MoneyEntryInput { RequestId = Guid.NewGuid(), CategoryId = cat.Id, Amount = 20m }]));

        var ids = new[] { res1.Ids[0], res2.Ids[0] };
        var targetDate = today.AddDays(-5);
        var targetTimestamp = new DateTimeOffset(targetDate.Year, targetDate.Month, targetDate.Day, 10, 0, 0, TimeSpan.Zero);

        var updated = await corrections.BulkChangeDateAsync(AdminUser, "expense", ids, targetTimestamp, "Bulk downtime backfill");
        await updated.Should().BeEqualTo(2);

        var exp1 = await db.Expenses.SingleAsync(e => e.Id == ids[0]);
        var exp2 = await db.Expenses.SingleAsync(e => e.Id == ids[1]);
        await exp1.OccurredAt.Should().BeEqualTo(targetTimestamp);
        await exp2.OccurredAt.Should().BeEqualTo(targetTimestamp);

        var logs = await db.RecordChanges.Where(r => r.RecordType == "expense" && ids.Contains(r.RecordId)).ToListAsync();
        await logs.Count.Should().BeEqualTo(2);
        await logs.All(l => l.Reason == "Bulk downtime backfill").Should().BeTrue();
    }

    [Test]
    public async Task GetCashBalanceAtAsync_AnchorsToCheckpoint_AndReflectsBackdatedMovements()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var drawer = await db.CashDrawers.FirstAsync(d => d.StockLocationId == 1);
        var baseCash = drawer.CurrentCash;

        // Perform a physical cash count at T0
        var t0 = DateTimeOffset.UtcNow.AddHours(-10);
        var count = new CashCount
        {
            CashDrawerId = drawer.Id,
            StockLocationId = drawer.StockLocationId ?? 1,
            BusinessDate = DateOnly.FromDateTime(t0.UtcDateTime),
            CountedCash = baseCash,
            ExpectedCash = baseCash,
            CountedAt = t0,
            CountedByUserId = 1,
            Status = CashCountStatus.Completed
        };
        db.CashCounts.Add(count);
        drawer.LastCountedAt = t0;
        drawer.LastCountedCash = baseCash;
        await db.SaveChangesAsync();

        // Query balance at T0 -> should equal baseCash (checkpoint lookup)
        var balT0 = await drawerService.GetCashBalanceAtAsync(AdminUser, drawer.Id, t0);
        await balT0.Should().BeEqualTo(baseCash);

        // Add a backdated movement at T0 + 2 hours
        var t1 = t0.AddHours(2);
        var date1 = DateOnly.FromDateTime(t1.UtcDateTime);
        var movement = new CashMovement
        {
            CashDrawerId = drawer.Id,
            Kind = CashMovementKind.CashIn,
            Amount = 350m,
            BalanceAfter = baseCash + 350m,
            BusinessDate = date1,
            CreatedAt = t1,
            Notes = "Midday float addition"
        };
        db.CashMovements.Add(movement);
        drawer.CurrentCash += 350m;
        await db.SaveChangesAsync();

        // Balance before t1 (e.g. t0 + 1 hr) should still be baseCash
        var balBefore = await drawerService.GetCashBalanceAtAsync(AdminUser, drawer.Id, t0.AddHours(1));
        await balBefore.Should().BeEqualTo(baseCash);

        // Balance at or after t1 (e.g. t0 + 3 hrs) should be baseCash + 350
        var balAfter = await drawerService.GetCashBalanceAtAsync(AdminUser, drawer.Id, t0.AddHours(3));
        await balAfter.Should().BeEqualTo(baseCash + 350m);
    }

    [Test]
    public async Task PhysicalCashCount_DeleteCount_ReAnchorsToPrecedingCount()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var drawer = await db.CashDrawers.FirstAsync(d => d.StockLocationId == 1);
        var baseCash = drawer.CurrentCash;

        // Step 1: Count 1 at T0: anchor at baseCash + 1000
        var t0 = DateTimeOffset.UtcNow.AddDays(-2);
        var count1 = new CashCount
        {
            RequestId = Guid.NewGuid(),
            CashDrawerId = drawer.Id,
            StockLocationId = drawer.StockLocationId ?? 1,
            BusinessDate = DateOnly.FromDateTime(t0.UtcDateTime),
            CountedCash = baseCash + 1000m,
            ExpectedCash = baseCash,
            CountedAt = t0,
            CountedByUserId = 1,
            Status = CashCountStatus.VarianceAccepted,
            AcceptedVariance = 1000m
        };
        db.CashCounts.Add(count1);
        await db.SaveChangesAsync();

        // Step 2: Movement +200 at T0 + 1 hr
        var move1 = new CashMovement
        {
            RequestId = Guid.NewGuid(),
            CashDrawerId = drawer.Id,
            BusinessDate = DateOnly.FromDateTime(t0.UtcDateTime),
            CreatedAt = t0.AddHours(1),
            Kind = CashMovementKind.Sale,
            Amount = 200m,
            BalanceAfter = baseCash + 1200m,
            Notes = "Sale 1",
            CreatedByUserId = 1
        };
        db.CashMovements.Add(move1);

        // Step 3: Count 2 at T0 + 2 hrs: anchor at 1500
        var t2 = t0.AddHours(2);
        var count2 = new CashCount
        {
            RequestId = Guid.NewGuid(),
            CashDrawerId = drawer.Id,
            StockLocationId = drawer.StockLocationId ?? 1,
            BusinessDate = DateOnly.FromDateTime(t2.UtcDateTime),
            CountedCash = 1500m,
            ExpectedCash = baseCash + 1200m,
            CountedAt = t2,
            CountedByUserId = 1,
            Status = CashCountStatus.VarianceAccepted,
            AcceptedVariance = 100m
        };
        db.CashCounts.Add(count2);
        drawer.LastCountedAt = t2;
        drawer.LastCountedCash = 1500m;
        drawer.CurrentCash = 1500m;
        await db.SaveChangesAsync();

        // While Count 2 is anchor, live balance is 1500
        var balWithCount2 = await CashDrawerService.CalculateLiveBalanceAsync(db, drawer, null);
        await balWithCount2.Should().BeEqualTo(1500m);

        // Now Admin deletes Count 2
        await drawerService.DeleteCountAsync(AdminUser, count2.Id);

        // After deleting Count 2, drawer must re-anchor to Count 1 (baseCash + 1000) + move1 (+200) = baseCash + 1200!
        var drawerAfter = await db.CashDrawers.AsNoTracking().FirstAsync(d => d.Id == drawer.Id);
        await drawerAfter.LastCountedAt.Should().BeEqualTo(t0);
        await drawerAfter.LastCountedCash.Should().BeEqualTo(baseCash + 1000m);
        await drawerAfter.CurrentCash.Should().BeEqualTo(baseCash + 1200m);
    }

    [Test]
    public async Task PhysicalCashCount_RetroactiveVoid_PlacesReversalInInterval_AndUpdatesVarianceDrift()
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();

        var drawer = await db.CashDrawers.FirstAsync(d => d.StockLocationId == 1);
        var zone = AccountingCalendar.Zone(null);
        var t0 = DateTimeOffset.UtcNow.AddHours(-6);
        drawer.OpeningDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t0, zone).DateTime).AddDays(-1);
        await db.SaveChangesAsync();
        var baseCash = drawer.CurrentCash;

        // Step 1: Create a sale, payment and movement at T0
        var sale = new Sale
        {
            ReceiptNumber = "REC-TEST-001",
            CashierId = 1,
            CustomerId = 1,
            StockLocationId = 1,
            Subtotal = 500m,
            TotalAmount = 500m,
            TenderedAmount = 500m,
            Status = SaleStatus.Completed,
            SoldAt = t0,
            CreatedAt = t0,
            CreatedByUserId = 1
        };
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        var salePayment = new Payment
        {
            SaleId = sale.Id,
            Amount = 500m,
            Method = PaymentMethod.Cash,
            PaidAt = t0,
            CreatedByUserId = 1
        };
        db.Payments.Add(salePayment);

        var saleMove = new CashMovement
        {
            RequestId = Guid.NewGuid(),
            CashDrawerId = drawer.Id,
            BusinessDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t0, zone).DateTime),
            CreatedAt = t0,
            Kind = CashMovementKind.Sale,
            Amount = 500m,
            BalanceAfter = baseCash + 500m,
            Notes = $"Test Sale #{sale.Id}",
            Payment = salePayment,
            CreatedByUserId = 1
        };
        db.CashMovements.Add(saleMove);
        drawer.CurrentCash += 500m;
        await db.SaveChangesAsync();

        // Step 2: Accept physical cash count at T0 + 1 hour with shortage of -500 (counted = baseCash, expected = baseCash + 500)
        var t1 = t0.AddHours(1);
        var countId = await drawerService.SubmitCountAsync(AdminUser, new CashCountInput
        {
            LocationId = drawer.StockLocationId ?? 1,
            CountedCash = baseCash,
            CountedAt = t1,
            IsBackdate = true,
            Notes = "Short by 500"
        });

        // Admin accepts the -500 shortage
        await drawerService.AcceptVarianceAsync(AdminUser, new AcceptVarianceInput
        {
            CountId = countId,
            Confirmed = true,
            Notes = "Shortage approved"
        });

        var acceptedCount = await db.CashCounts.Include(c => c.VarianceMovement).AsNoTracking().FirstAsync(c => c.Id == countId);
        await acceptedCount.AcceptedVariance.Should().BeEqualTo(-500m);
        await acceptedCount.VarianceMovementId.HasValue.Should().BeTrue();
        await acceptedCount.VarianceMovement!.Amount.Should().BeEqualTo(-500m);

        // Drawer balance should be anchored to baseCash
        var drawerCurrent = await db.CashDrawers.AsNoTracking().FirstAsync(d => d.Id == drawer.Id);
        await drawerCurrent.CurrentCash.Should().BeEqualTo(baseCash);

        // Step 3: Admin retroactively voids the sale at a later time (e.g., T0 + 3 hours)
        await CashDrawerService.ReverseAsync(db, "sale", sale.Id, 1, "Customer cancelled mistakenly", saleMove.BusinessDate, drawer.StockLocationId, default);

        // Verify the reversal movement:
        // 1. Reversal must take the original BusinessDate
        // 2. CreatedAt must sort directly after the original (+1ms)
        var reversal = await db.CashMovements.AsNoTracking().FirstAsync(m => m.ReversesMovementId == saleMove.Id);
        await reversal.BusinessDate.Should().BeEqualTo(saleMove.BusinessDate);
        await reversal.CreatedAt.Should().BeEqualTo(saleMove.CreatedAt.AddMilliseconds(1));
        await reversal.Kind.Should().BeEqualTo(CashMovementKind.Reversal);
        await reversal.Amount.Should().BeEqualTo(-500m);

        // Step 4: Verify variance drift on the count
        // Net movements before count are now (sale +500 + reversal -500) = 0.
        // Live expected cash is now baseCash + 0 = baseCash.
        // Live variance is now baseCash - baseCash = 0.
        // Accepted variance was -500, but live variance is now 0 (Variance drift!).
        var refreshedPage = await drawerService.ReadAsync(AdminUser);
        var reviewedCount = refreshedPage.CountHistory.First(c => c.Id == countId);
        await reviewedCount.AcceptedVariance.Should().BeEqualTo(-500m);
        await reviewedCount.HasVarianceDrift.Should().BeTrue();
        await reviewedCount.LiveVariance.Should().BeEqualTo(0m);

        // The count's variance movement amount should have synced to 0
        var syncedVarMove = await db.CashMovements.AsNoTracking().FirstAsync(m => m.Id == acceptedCount.VarianceMovementId!.Value);
        await syncedVarMove.Amount.Should().BeEqualTo(0m);
    }

    [Test]
    public async Task CashDrawer_ExpectedAt_FollowsLedgerIntervalOrder_MatchingStatementDayOpening()
    {
        await using var scope = CreateScope();
        var drawerService = scope.ServiceProvider.GetRequiredService<CashDrawerService>();
        var statementService = scope.ServiceProvider.GetRequiredService<StatementService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var drawer = await db.CashDrawers.FirstAsync(d => d.StockLocationId == 1);
        var baseCash = drawer.CurrentCash;

        var day1Date = new DateOnly(2026, 11, 9);
        var day2Date = new DateOnly(2026, 11, 10);
        var day1Time = new DateTimeOffset(2026, 11, 9, 13, 0, 0, TimeSpan.FromHours(6));
        var day2Midnight = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(6));
        var day2Night = new DateTimeOffset(2026, 11, 10, 20, 36, 0, TimeSpan.FromHours(6));

        // Day 1: Add cash movement +3,600
        var m1 = new CashMovement
        {
            RequestId = Guid.NewGuid(),
            CashDrawerId = drawer.Id,
            BusinessDate = day1Date,
            CreatedAt = day1Time,
            Kind = CashMovementKind.OwnerContribution,
            Amount = 3600m,
            Notes = "Day 1 Cash In",
            CreatedByUserId = 1
        };
        db.CashMovements.Add(m1);

        // Day 2 (evening): Add supplier payments totaling -2,500
        var m2 = new CashMovement
        {
            RequestId = Guid.NewGuid(),
            CashDrawerId = drawer.Id,
            BusinessDate = day2Date,
            CreatedAt = day2Night,
            Kind = CashMovementKind.SupplierPayment,
            Amount = -1000m,
            Notes = "Supplier payment #31",
            CreatedByUserId = 1
        };
        var m3 = new CashMovement
        {
            RequestId = Guid.NewGuid(),
            CashDrawerId = drawer.Id,
            BusinessDate = day2Date,
            CreatedAt = day2Night,
            Kind = CashMovementKind.SupplierPayment,
            Amount = -1000m,
            Notes = "Supplier payment #18",
            CreatedByUserId = 1
        };
        var m4 = new CashMovement
        {
            RequestId = Guid.NewGuid(),
            CashDrawerId = drawer.Id,
            BusinessDate = day2Date,
            CreatedAt = day2Night,
            Kind = CashMovementKind.SupplierPayment,
            Amount = -500m,
            Notes = "Supplier payment #66",
            CreatedByUserId = 1
        };
        db.CashMovements.AddRange(m2, m3, m4);
        drawer.CurrentCash += (3600m - 2500m);
        await db.SaveChangesAsync();

        // 1. Calculate expected book cash as of Day 2 midnight (12:00 AM)
        var expectedAtMidnight = await drawerService.GetExpectedAtAsync(AdminUser, drawer.StockLocationId ?? 1, day2Midnight);

        // Expected book cash at midnight of Day 2 must include Day 1 movements (+3600), but EXCLUDE Day 2 20:36 night movements!
        // It also correctly excludes drawer opening balance since drawer.OpeningDate is today (after 10 Nov 2026).
        await expectedAtMidnight.Should().BeEqualTo(3600m);

        // 2. Query Statement for Day 2 (10 Nov 2026 - 12 Nov 2026)
        var statement = await statementService.ReadAsync(AdminUser, "cash", "custom", start: day2Date, end: new DateOnly(2026, 11, 12), drawerId: drawer.Id);

        // Opening balance in statement for 10 Nov must exactly match expectedAtMidnight
        await statement.Opening.Should().BeEqualTo(expectedAtMidnight);
    }
}
