using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using POSApp.Client.DTOs;
using POSApp.Services;

namespace POSApp.Tests.Unit.Validation;

public class DraftServiceValidationTests
{
    [Test]
    public async Task ReadAsync_WhenKindIsNotStockTake_ThrowsValidationException()
    {
        var service = new DraftService(null!);
        var actor = new ClaimsPrincipal();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.ReadAsync(actor, "sales"));
        await ex!.Message.Should().BeEqualTo("Server drafts are only supported for stock takes.");
    }

    [Test]
    public async Task SaveAsync_WhenKindIsNotStockTake_ThrowsValidationException()
    {
        var service = new DraftService(null!);
        var actor = new ClaimsPrincipal();
        var input = new EditorDraftInput(0, "{}", Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(actor, "receiving", input));
        await ex!.Message.Should().BeEqualTo("Server drafts are only supported for stock takes.");
    }
}
