using System.Net;
using System.Net.Http.Json;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Endpoints;

public class InventoryEndpointsTests : PosIntegrationTestBase
{
    [Test]
    public async Task GetReceivingOptions_WhenAuthenticated_ReturnsLocationsAndSuppliers()
    {
        var response = await InventoryClient.GetAsync("/api/inventory/options");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var options = await response.Content.ReadFromJsonAsync<ReceivingOptions>();
        await options.Should().NotBeNull();
        await options!.Locations.Count.Should().BeGreaterThanOrEqualTo(1);
        await options.Suppliers.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task CreateLocation_WhenAuthenticated_CreatesLocationAndReturnsId()
    {
        var input = new LocationInput("North Depot", CanSell: true);

        var response = await InventoryClient.PostAsJsonAsync("/api/inventory/locations", input);

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var locationId = await response.Content.ReadFromJsonAsync<int>();
        await locationId.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task GetCategories_WhenAuthenticated_ReturnsCategories()
    {
        var response = await InventoryClient.GetAsync("/api/inventory/items/categories");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var categories = await response.Content.ReadFromJsonAsync<Lookup[]>();
        await categories.Should().NotBeNull();
        await categories!.Length.Should().BeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task CreateProduct_PersistsProductAndReturnsItemRow()
    {
        var input = new ProductInput
        {
            Name = "Mechanical Keyboard RGB",
            Unit = "piece",
            CostPrice = 1200m,
            SellingPrice = 1800m,
            SupplierId = 1,
            CategoryId = 1,
            GenerateBarcodeWhenEmpty = true
        };

        var response = await InventoryClient.PostAsJsonAsync("/api/inventory/items", input);

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var item = await response.Content.ReadFromJsonAsync<ItemRow>();
        await item.Should().NotBeNull();
        await item!.Name.Should().BeEqualTo("Mechanical Keyboard RGB");
        await item.Cost.Should().BeEqualTo(1200m);
        await item.Price.Should().BeEqualTo(1800m);
        await item.Barcode.Should().NotBeNull();
    }

    [Test]
    public async Task ReceiveStock_UpdatesStockAndMovingAverageCost()
    {
        // 1. Create a product with base cost 100
        var input = new ProductInput
        {
            Name = "Wireless Mouse",
            Unit = "piece",
            CostPrice = 100m,
            SellingPrice = 250m,
            SupplierId = 1,
            CategoryId = 1
        };

        var createResponse = await InventoryClient.PostAsJsonAsync("/api/inventory/items", input);
        var item = await createResponse.Content.ReadFromJsonAsync<ItemRow>();
        await item.Should().NotBeNull();

        // 2. Receive 10 units @ 100
        var receive1 = new ReceiveCommand(
            RequestId: Guid.NewGuid(),
            SupplierId: 1,
            LocationId: 1,
            Lines: [new ReceiveLine(item!.Id, Quantity: 10, UnitCost: 100m)],
            Payment: 1000m,
            Method: PaymentMethod.Cash,
            Invoice: "BATCH-1",
            Notes: "First shipment");

        var res1 = await InventoryClient.PostAsJsonAsync("/api/inventory/receipts", receive1);
        await res1.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);

        // 3. Receive 10 units @ 200 => moving average cost should become (10*100 + 10*200) / 20 = 150
        var receive2 = new ReceiveCommand(
            RequestId: Guid.NewGuid(),
            SupplierId: 1,
            LocationId: 1,
            Lines: [new ReceiveLine(item.Id, Quantity: 10, UnitCost: 200m)],
            Payment: 2000m,
            Method: PaymentMethod.Cash,
            Invoice: "BATCH-2",
            Notes: "Second shipment");

        var res2 = await InventoryClient.PostAsJsonAsync("/api/inventory/receipts", receive2);
        await res2.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);

        // 4. Verify item now has stock 20 and moving average cost 150
        var getResponse = await InventoryClient.GetAsync($"/api/inventory/items?locationId=1&search={item.Id}");
        var items = await getResponse.Content.ReadFromJsonAsync<ItemRow[]>();
        await items.Should().NotBeNull();

        var updated = items!.First(i => i.Id == item.Id);
        await updated.Stock.Should().BeEqualTo(20);
        await updated.Cost.Should().BeEqualTo(150m);
    }
}
