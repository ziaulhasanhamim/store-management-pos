using POSApp.Persistence.Entities;

namespace POSApp.Tests.Unit.Common;

public class NameNormalizationTests
{
    [Test]
    public async Task Normalize_TrimsWhitespaceAndUppercases()
    {
        var input = "   Fresh Apples 1kg   ";
        var result = NameNormalization.Normalize(input);

        await result.Should().BeEqualTo("FRESH APPLES 1KG");
    }

    [Test]
    public async Task Normalize_WithAlreadyNormalizedText_RemainsUnchanged()
    {
        var input = "STANDARD";
        var result = NameNormalization.Normalize(input);

        await result.Should().BeEqualTo("STANDARD");
    }
}
