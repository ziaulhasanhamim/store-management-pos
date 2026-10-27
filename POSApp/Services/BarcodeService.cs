using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using POSApp.Persistence;
using ZXing;
using ZXing.Common;

namespace POSApp.Services;

public sealed class BarcodeService(IServiceScopeFactory scopes)
{
    public static string FromProductId(int id)
    {
        if (id is < 1 or > 9999999)
        {
            throw new ValidationException("ID barcodes support product IDs from 1 to 9,999,999. Enter a manual barcode for larger IDs.");
        }

        var body = id.ToString("D7");
        var sum = body.Select((digit, index) => (digit - '0') * (index % 2 == 0 ? 3 : 1)).Sum();
        return $"{body}{(10 - sum % 10) % 10}";
    }

    public static string Svg(string barcode)
    {
        var isEan = barcode.Length == 8 && barcode.All(char.IsAsciiDigit)
            && (barcode.Take(7).Select((digit, index) => (digit - '0') * (index % 2 == 0 ? 3 : 1)).Sum() + barcode[7] - '0') % 10 == 0;
        var writer = new BarcodeWriterSvg
        {
            Format = isEan ? BarcodeFormat.EAN_8 : BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Width = 300, Height = 65, Margin = 10, PureBarcode = true }
        };
        return writer.Write(barcode).Content.Replace(
            "<svg ",
            $"<svg class=\"barcode-{(isEan ? "ean8" : "code128")}\" preserveAspectRatio=\"none\" ",
            StringComparison.Ordinal);
    }

    public async Task GenerateMissingAsync(ClaimsPrincipal actor, IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        await StoreOperation.RunAsync(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
            if (ids.Count is < 1 or > 200)
            {
                throw new ValidationException("Select between 1 and 200 products.");
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var products = await db.Products.Where(product => ids.Contains(product.Id) && !product.IsDeleted).ToListAsync(ct);
            if (products.Count != ids.Distinct().Count())
            {
                throw new ValidationException("A selected product is no longer available.");
            }

            var targets = products.Where(product => string.IsNullOrWhiteSpace(product.Barcode)).ToList();
            if (targets.Count > 0)
            {
                var candidateList = targets.Select(p => (Product: p, Code: FromProductId(p.Id))).ToList();
                var candidateCodes = candidateList.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Single batch query replaces N individual queries
                var existingCodes = (await db.Products.AsNoTracking()
                    .Where(p => candidateCodes.Contains(p.Barcode!))
                    .Select(p => p.Barcode!)
                    .ToListAsync(ct))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var assignedInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var now = DateTimeOffset.UtcNow;

                foreach (var (product, code) in candidateList)
                {
                    if (existingCodes.Contains(code) || !assignedInBatch.Add(code)
                        || products.Any(other => other.Id != product.Id && other.Barcode == code))
                    {
                        throw new ValidationException($"The generated code for {product.Name} is already used. Set a manual barcode.");
                    }

                    product.Barcode = code;
                    product.UpdatedAt = now;
                }
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    public async Task<IReadOnlyList<BarcodeLabel>> LabelsAsync(ClaimsPrincipal actor, int[] ids, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await StoreAccess.RequireAsync(db, actor, AppRoles.InventoryManager, ct);
        return await db.Products.AsNoTracking()
            .Where(product => ids.Contains(product.Id) && !product.IsDeleted && product.Barcode != null)
            .OrderBy(product => product.Name)
            .Select(product => new BarcodeLabel(product.Id, product.Name, product.Barcode!))
            .ToListAsync(ct);
    }
}
