using System.Net;
using System.Net.Http.Json;
using POSApp.Client.DTOs;
using POSApp.Client.Enums;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Endpoints;

public class SalesEndpointsTests : PosIntegrationTestBase
{
    [Test]
    public async Task GetOptions_WhenAnonymous_ReturnsUnauthorized()
    {
        var response = await AnonymousClient.GetAsync("/api/sales/options");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetOptions_WhenAuthenticatedAsSales_ReturnsOptions()
    {
        var response = await SalesClient.GetAsync("/api/sales/options");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var options = await response.Content.ReadFromJsonAsync<SalesOptions>();
        await options.Should().NotBeNull();
        await options!.Locations.Length.Should().BeGreaterThanOrEqualTo(1);
        await options.Customers.Length.Should().BeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task AddCustomer_CreatesCustomerAndReturnsId()
    {
        var input = new CustomerInput("Alice Green", "01700000000", 0m);

        var response = await SalesClient.PostAsJsonAsync("/api/sales/customers", input);

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var customerId = await response.Content.ReadFromJsonAsync<int>();
        await customerId.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task SaveDraft_SavesDraftSuccessfullyAndCanBeRetrieved()
    {
        var draftId = Guid.NewGuid();
        var command = new SalesCommand(
            RequestId: draftId,
            LocationId: 1,
            CustomerId: 1,
            Lines: [new SalesLine(ProductId: 1, Quantity: 1, UnitPrice: 150m)],
            Discount: 0m,
            Payment: 0m,
            Method: PaymentMethod.Cash,
            Notes: "Draft for later review");

        var saveResponse = await SalesClient.PostAsJsonAsync("/api/sales/drafts", command);
        await saveResponse.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);

        var getResponse = await SalesClient.GetAsync($"/api/sales/drafts/{draftId}");
        await getResponse.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);

        var retrieved = await getResponse.Content.ReadFromJsonAsync<SalesDraft>();
        await retrieved.Should().NotBeNull();
        await retrieved!.RequestId.Should().BeEqualTo(draftId);
        await retrieved.Notes.Should().BeEqualTo("Draft for later review");
    }

    [Test]
    public async Task CompleteSale_WhenValid_DecrementsStockAndReturnsCompletedResult()
    {
        // 1. Create a product first via InventoryClient
        var productInput = new ProductInput
        {
            Name = "Smartphone Charger 65W",
            Unit = "piece",
            CostPrice = 200m,
            SellingPrice = 350m,
            SupplierId = 1,
            CategoryId = 1
        };

        var itemResponse = await InventoryClient.PostAsJsonAsync("/api/inventory/items", productInput);
        await itemResponse.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var item = await itemResponse.Content.ReadFromJsonAsync<ItemRow>();
        await item.Should().NotBeNull();

        // 2. Receive 10 units of stock
        var receiveCommand = new ReceiveCommand(
            RequestId: Guid.NewGuid(),
            SupplierId: 1,
            LocationId: 1,
            Lines: [new ReceiveLine(item!.Id, Quantity: 10, UnitCost: 200m)],
            Payment: 2000m,
            Method: PaymentMethod.Cash,
            Invoice: "INV-TEST-001",
            Notes: "Initial batch");

        var receiveResponse = await InventoryClient.PostAsJsonAsync("/api/inventory/receipts", receiveCommand);
        await receiveResponse.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);

        // 3. Complete sale of 2 units via SalesClient
        var saleCommand = new SalesCommand(
            RequestId: Guid.NewGuid(),
            LocationId: 1,
            CustomerId: 1,
            Lines: [new SalesLine(item.Id, Quantity: 2, UnitPrice: 350m)],
            Discount: 0m,
            Payment: 700m,
            Method: PaymentMethod.Cash,
            Notes: "Walk-in sale");

        var saleResponse = await SalesClient.PostAsJsonAsync("/api/sales/complete", saleCommand);
        await saleResponse.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);

        var result = await saleResponse.Content.ReadFromJsonAsync<SalesResult>();
        await result.Should().NotBeNull();
        await result!.Completed.Should().BeEqualTo(true);
        await result.Total.Should().BeEqualTo(700m);
        await result.Paid.Should().BeEqualTo(700m);
        await result.Due.Should().BeEqualTo(0m);

        // 4. Verify stock remaining at location is now 8 units
        var itemsResponse = await InventoryClient.GetAsync($"/api/inventory/items?locationId=1&search={item.Id}");
        await itemsResponse.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var items = await itemsResponse.Content.ReadFromJsonAsync<ItemRow[]>();
        await items.Should().NotBeNull();
        var updatedItem = items!.First(i => i.Id == item.Id);
        await updatedItem.Stock.Should().BeEqualTo(8);
    }
}
