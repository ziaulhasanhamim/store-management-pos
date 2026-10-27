using System.Net;
using System.Net.Http.Json;
using POSApp.Client.DTOs;
using POSApp.Tests.Integration.Infrastructure;

namespace POSApp.Tests.Integration.Endpoints;

public class DraftEndpointsTests : PosIntegrationTestBase
{
    [Test]
    public async Task GetIdentity_WhenAuthenticated_ReturnsUserId()
    {
        var response = await InventoryClient.GetAsync("/api/drafts/identity");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var identity = await response.Content.ReadFromJsonAsync<string>();
        await identity.Should().BeEqualTo("2");
    }

    [Test]
    public async Task ReadDraft_WhenNonStockTakeKind_ReturnsBadRequest()
    {
        var response = await InventoryClient.GetAsync("/api/drafts/sales");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SaveDraft_WhenNonStockTakeKind_ReturnsBadRequest()
    {
        var input = new EditorDraftInput(Revision: 0, Payload: "{\"note\":\"test\"}", SaveId: Guid.NewGuid());
        var response = await InventoryClient.PostAsJsonAsync("/api/drafts/sales", input);

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ReadDraft_WhenNoDraftExists_ReturnsNoContent()
    {
        var response = await InventoryClient.GetAsync("/api/drafts/stock-take:1");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task SaveAndReadDraft_PersistsStockTakeDraftDocument()
    {
        var saveId = Guid.NewGuid();
        var input = new EditorDraftInput(Revision: 0, Payload: "{\"note\":\"test stock take draft\"}", SaveId: saveId);

        var saveResponse = await InventoryClient.PostAsJsonAsync("/api/drafts/stock-take:1", input);
        await saveResponse.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);

        var savedDoc = await saveResponse.Content.ReadFromJsonAsync<EditorDraftDocument>();
        await savedDoc.Should().NotBeNull();
        await savedDoc!.Payload.Should().BeEqualTo("{\"note\":\"test stock take draft\"}");
        await savedDoc.SaveId.Should().BeEqualTo(saveId);

        var getResponse = await InventoryClient.GetAsync("/api/drafts/stock-take:1");
        await getResponse.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);

        var retrievedDoc = await getResponse.Content.ReadFromJsonAsync<EditorDraftDocument>();
        await retrievedDoc.Should().NotBeNull();
        await retrievedDoc!.Payload.Should().BeEqualTo("{\"note\":\"test stock take draft\"}");
    }
}
