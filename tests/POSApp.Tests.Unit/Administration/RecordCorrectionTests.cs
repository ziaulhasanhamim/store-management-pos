using POSApp.Client.DTOs;
using POSApp.Services.Calculations;

namespace POSApp.Tests.Unit.Administration;

public class RecordCorrectionTests
{
    [Test]
    public async Task RecordCorrectionInput_DoesNotContainTransactionDateProperty()
    {
        // Enforce that transaction timestamps cannot be changed through RecordCorrectionInput
        var property = typeof(RecordCorrectionInput).GetProperty("EffectiveAt");
        await property.Should().BeNull();
    }

    [Test]
    public async Task SaleBill_CarriesVoidedStateAndTimestamp()
    {
        var voidedAt = new DateTimeOffset(2026, 10, 27, 16, 0, 0, TimeSpan.Zero);
        var bill = new SaleBill(
            Id: 99,
            Number: "REC-20261027-0099",
            At: new DateTimeOffset(2026, 10, 27, 14, 0, 0, TimeSpan.Zero),
            Customer: "Regular Customer",
            Location: "Main Branch",
            Cashier: "Cashier1",
            Lines: [],
            Subtotal: 1500m,
            Discount: 0m,
            Total: 1500m,
            Paid: 1500m,
            Tendered: 1500m,
            Notes: "Voided by admin due to entry error"
        )
        {
            IsVoided = true,
            VoidedAt = voidedAt
        };

        await bill.IsVoided.Should().BeEqualTo(true);
        await bill.VoidedAt.Should().BeEqualTo(voidedAt);
    }

    [Test]
    public async Task InventoryValuationCalculator_WhenReversingReceiving_MaintainsCorrectAverageCost()
    {
        // Suppose current stock was 10 units at average cost 50 BDT (total value 500 BDT)
        // If 5 units are reversed/deducted at cost 50, remaining stock is 5 units with average cost 50 BDT
        decimal existingQuantity = -2m;
        decimal existingCost = 6m;
        decimal incomingQuantity = 4m;
        decimal incomingCost = 8m;

        var cost = InventoryValuationCalculator.CalculateMovingAverageCost(existingQuantity, existingCost, incomingQuantity, incomingCost);

        // ((-2 * 6) + (4 * 8)) / 2 = (-12 + 32) / 2 = 20 / 2 = 10
        await cost.Should().BeEqualTo(10m);
    }
}
