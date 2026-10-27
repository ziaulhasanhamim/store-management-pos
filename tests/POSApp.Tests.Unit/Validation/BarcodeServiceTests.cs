using System.ComponentModel.DataAnnotations;
using POSApp.Services;

namespace POSApp.Tests.Unit.Validation;

public class BarcodeServiceTests
{
    [Test]
    public async Task FromProductId_WithValidId_GeneratesEan8WithChecksum()
    {
        // ID 1 should format as 7 digits + check digit 7 => "00000017"
        var code = BarcodeService.FromProductId(1);

        await code.Should().BeEqualTo("00000017");
        await code.Length.Should().BeEqualTo(8);
    }

    [Test]
    public async Task FromProductId_WithLargeValidId_GeneratesEan8()
    {
        var code = BarcodeService.FromProductId(1234567);

        // index 0: 1*3 = 3
        // index 1: 2*1 = 2
        // index 2: 3*3 = 9
        // index 3: 4*1 = 4
        // index 4: 5*3 = 15
        // index 5: 6*1 = 6
        // index 6: 7*3 = 21
        // sum = 3+2+9+4+15+6+21 = 60
        // check digit = (10 - 60 % 10) % 10 = 0
        await code.Should().BeEqualTo("12345670");
    }

    [Test]
    public async Task FromProductId_WhenZeroOrNegative_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => BarcodeService.FromProductId(0));
        Assert.Throws<ValidationException>(() => BarcodeService.FromProductId(-1));
    }

    [Test]
    public async Task FromProductId_WhenExceedsMaximum_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() => BarcodeService.FromProductId(10000000));
    }

    [Test]
    public async Task Svg_WhenGivenEan8Barcode_GeneratesEan8Svg()
    {
        var svg = BarcodeService.Svg("00000017");

        await svg.Should().NotBeNull();
        await svg.Contains("class=\"barcode-ean8\"").Should().BeEqualTo(true);
        await svg.Contains("</svg>").Should().BeEqualTo(true);
    }

    [Test]
    public async Task Svg_WhenGivenNonEanBarcode_GeneratesCode128Svg()
    {
        var svg = BarcodeService.Svg("ITEM-CUSTOM-99");

        await svg.Should().NotBeNull();
        await svg.Contains("class=\"barcode-code128\"").Should().BeEqualTo(true);
        await svg.Contains("</svg>").Should().BeEqualTo(true);
    }
}
