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
        var response = await SalesClient.GetAsync("/api/drafts/identity");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);
        var identity = await response.Content.ReadFromJsonAsync<string>();
        await identity.Should().BeEqualTo("3");
    }

    [Test]
    public async Task ReadDraft_WhenNoDraftExists_ReturnsNoContent()
    {
        var response = await SalesClient.GetAsync("/api/drafts/sales");

        await response.StatusCode.Should().BeEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task SaveAndReadDraft_PersistsDraftDocument()
    {
        var saveId = Guid.NewGuid();
        var input = new EditorDraftInput(Revision: 0, Payload: "{\"note\":\"test draft payload\"}", SaveId: saveId);

        var saveResponse = await SalesClient.PostAsJsonAsync("/api/drafts/sales", input);
        await saveResponse.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);

        var savedDoc = await saveResponse.Content.ReadFromJsonAsync<EditorDraftDocument>();
        await savedDoc.Should().NotBeNull();
        await savedDoc!.Payload.Should().BeEqualTo("{\"note\":\"test draft payload\"}");
        await savedDoc.SaveId.Should().BeEqualTo(saveId);

        var getResponse = await SalesClient.GetAsync("/api/drafts/sales");
        await getResponse.StatusCode.Should().BeEqualTo(HttpStatusCode.OK);

        var retrievedDoc = await getResponse.Content.ReadFromJsonAsync<EditorDraftDocument>();
        await retrievedDoc.Should().NotBeNull();
        await retrievedDoc!.Payload.Should().BeEqualTo("{\"note\":\"test draft payload\"}");
    }
}
