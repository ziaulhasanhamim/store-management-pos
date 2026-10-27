using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace POSApp.Services;

/// <summary>
/// Replays a complete operation, including its scope, reads and transaction.
/// Never retry SaveChanges alone: its tracked values belong to the failed attempt.
/// </summary>
public static class StoreOperation
{
    private const int MaximumAttempts = 4;

    public static string Fingerprint<T>(T input)
    {
        using var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(input));
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, document.RootElement);
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(System.Text.Json.Utf8JsonWriter writer, System.Text.Json.JsonElement value)
    {
        switch (value.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case System.Text.Json.JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            case System.Text.Json.JsonValueKind.Number when value.TryGetDecimal(out var number):
                // Browser JSON and PostgreSQL may preserve different decimal scales.
                writer.WriteRawValue(number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    public static void CheckReplay(int actorId, int? originalActor, string? originalHash, string hash)
    {
        if (originalActor != actorId)
        {
            throw new UnauthorizedAccessException("This submission belongs to another user.");
        }
        if (originalHash is not null && originalHash != hash)
        {
            throw new ValidationException("This submission was already saved with different values. Start a new entry instead of reusing it.");
        }
    }

    public static async Task RunAsync(Func<Task> operation, CancellationToken ct = default)
    {
        await RunAsync(async () =>
        {
            await operation();
            return true;
        }, ct);
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await operation();
            }
            catch (Exception exception) when (Retryable(exception))
            {
                if (attempt == MaximumAttempts)
                {
                    throw new ValidationException(
                        "Another user is updating the same records. Your entries are kept. Please retry the same submission.",
                        exception);
                }

                // A short, bounded jitter prevents cashiers repeatedly colliding.
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (1 << (attempt - 1))
                    + Random.Shared.Next(10, 40)), ct);
            }
        }
    }

    internal static bool Retryable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is not PostgresException postgres)
            {
                continue;
            }

            if (postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            {
                return true;
            }

            // Concurrent first creation or identical submissions can surface as a
            // unique violation. Retry only known business identity constraints.
            if (postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && postgres.ConstraintName is { } constraint
                && (constraint == "IX_ImportJobs_ActiveKey"
                    || constraint == "PK_ImportJobs"
                    || constraint.EndsWith("_RequestId", StringComparison.Ordinal)
                    || constraint is "IX_Sales_ReceiptNumber" or "IX_Purchases_PurchaseNumber"
                        or "IX_Payments_Reference" or "IX_SupplierPayments_Reference"
                        or "IX_InventoryBalances_StockLocationId_ProductId"
                        or "IX_SupplierBalances_SupplierId" or "IX_CustomerBalances_CustomerId"
                        or "IX_EditorDrafts_UserId_Kind" or "IX_BankAccounts_Code" or "IX_StaffAccounts_EmployeeId"
                        or "IX_CashDrawers_StockLocationId" or "IX_MonthlySalaries_EmployeeId_Month"
                        or "IX_DayFinances_Date" or "IX_FinanceClosings_Kind_Date"))
            {
                return true;
            }
        }

        return false;
    }
}
