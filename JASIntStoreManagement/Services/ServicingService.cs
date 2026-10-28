using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using JASIntStoreManagement.Client.DTOs;
using JASIntStoreManagement.Client.Enums;
using JASIntStoreManagement.Client.Security;
using JASIntStoreManagement.Persistence;
using JASIntStoreManagement.Persistence.Entities;

namespace JASIntStoreManagement.Services;

public sealed class ServicingService(IServiceScopeFactory scopes, IConfiguration configuration)
{
    public async Task<IReadOnlyList<TechnicianDto>> GetTechniciansAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = db.Technicians.AsNoTracking().Where(t => !t.IsDeleted);
        if (!includeArchived)
        {
            query = query.Where(t => !t.IsArchived);
        }

        return await query
            .OrderBy(t => t.Name)
            .Select(t => new TechnicianDto(
                t.Id,
                t.Name,
                t.Phone,
                t.DefaultCommissionRate,
                t.IsArchived,
                t.ServicingEntries.Count,
                t.ServicingEntries.Sum(j => (decimal?)j.ShopProfit + j.TechnicianCut) ?? 0m,
                t.ServicingEntries.Sum(j => (decimal?)j.TechnicianCut) ?? 0m))
            .ToListAsync(ct);
    }

    public async Task<int> SaveTechnicianAsync(ClaimsPrincipal actor, TechnicianInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new ValidationException("Technician name is required.");
        }

        if (input.DefaultCommissionRate < 0 || input.DefaultCommissionRate > 100)
        {
            throw new ValidationException("Commission rate must be between 0% and 100%.");
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppRoles.Admin, ct);

        var normalized = NameNormalization.Normalize(input.Name);

        var duplicate = await db.Technicians
            .AnyAsync(t => t.Id != input.Id && !t.IsDeleted && t.NormalizedName == normalized, ct);
        if (duplicate)
        {
            throw new ValidationException($"A technician with name '{input.Name}' already exists.");
        }

        Technician technician;
        if (input.Id is { } id and > 0)
        {
            technician = await db.Technicians.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
                ?? throw new ValidationException("Technician not found.");

            technician.Name = input.Name.Trim();
            technician.NormalizedName = normalized;
            technician.Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();
            technician.DefaultCommissionRate = input.DefaultCommissionRate;
            technician.IsArchived = input.IsArchived;
            technician.UpdatedAt = DateTimeOffset.UtcNow;
            technician.UpdatedByUserId = actorId;
        }
        else
        {
            technician = new Technician
            {
                Name = input.Name.Trim(),
                NormalizedName = normalized,
                Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
                DefaultCommissionRate = input.DefaultCommissionRate,
                IsArchived = input.IsArchived,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedByUserId = actorId
            };
            db.Technicians.Add(technician);
        }

        await db.SaveChangesAsync(ct);
        return technician.Id;
    }

    public async Task<MobileServicingPageData> GetMobileServicingPageDataAsync(
        ClaimsPrincipal actor,
        DateOnly? date = null,
        int? locationId = null,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppPolicies.MakeSales, ct);

        var activeDate = date ?? await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);

        var locations = await db.StockLocations
            .Where(loc => loc.CanSell && !loc.IsDeleted)
            .OrderBy(loc => loc.Name)
            .Select(loc => new StockLocationOption(loc.Id, loc.Name))
            .ToListAsync(ct);

        var selectedLocationId = locationId;
        if (selectedLocationId == null)
        {
            var userLocation = await db.Users
                .Where(u => u.Id == actorId)
                .Select(u => u.CashLocationId)
                .SingleOrDefaultAsync(ct);
            selectedLocationId = userLocation ?? locations.FirstOrDefault()?.Id;
        }

        var technicians = await db.Technicians
            .Where(t => !t.IsDeleted && !t.IsArchived)
            .OrderBy(t => t.Name)
            .Select(t => new TechnicianDto(
                t.Id,
                t.Name,
                t.Phone,
                t.DefaultCommissionRate,
                t.IsArchived,
                t.ServicingEntries.Count,
                t.ServicingEntries.Sum(j => (decimal?)j.ShopProfit + j.TechnicianCut) ?? 0m,
                t.ServicingEntries.Sum(j => (decimal?)j.TechnicianCut) ?? 0m))
            .ToListAsync(ct);

        var entriesQuery = db.MobileServicingEntries
            .AsNoTracking()
            .Include(j => j.Location)
            .Include(j => j.Technician)
            .Where(j => j.Date == activeDate);

        if (selectedLocationId.HasValue)
        {
            entriesQuery = entriesQuery.Where(j => j.LocationId == selectedLocationId.Value);
        }

        var entries = await entriesQuery
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new MobileServicingEntryDto(
                j.Id,
                j.Date,
                j.LocationId,
                j.Location != null ? j.Location.Name : "Unknown",
                j.TechnicianId,
                j.Technician != null ? j.Technician.Name : "Unknown",
                j.TotalAmount,
                j.TotalCost,
                j.CommissionRate,
                j.TotalAmount - j.TotalCost,
                j.TechnicianCut,
                j.ShopProfit,
                j.IsDeposited,
                j.DepositedAt,
                j.CreatedAt,
                j.Notes))
            .ToListAsync(ct);

        var totalAmount = entries.Sum(e => e.TotalAmount);
        var totalCost = entries.Sum(e => e.TotalCost);
        var totalProfit = entries.Sum(e => e.Profit);
        var totalTechCut = entries.Sum(e => e.TechnicianCut);
        var totalShopProfit = entries.Sum(e => e.ShopProfit);
        var undepositedShopProfit = entries.Where(e => !e.IsDeposited).Sum(e => e.ShopProfit);
        var depositedShopProfit = entries.Where(e => e.IsDeposited).Sum(e => e.ShopProfit);

        var summary = new MobileServicingSummaryDto(
            TechnicianCount: entries.Select(e => e.TechnicianId).Distinct().Count(),
            TotalServicingAmount: totalAmount,
            TotalCost: totalCost,
            TotalProfit: totalProfit,
            TotalTechnicianCut: totalTechCut,
            TotalShopProfit: totalShopProfit,
            UndepositedShopProfit: undepositedShopProfit,
            DepositedShopProfit: depositedShopProfit);

        return new MobileServicingPageData(
            Date: activeDate,
            LocationId: selectedLocationId,
            Locations: locations,
            Technicians: technicians,
            Entries: entries,
            Summary: summary);
    }

    public async Task<int> SaveMobileServicingEntryAsync(ClaimsPrincipal actor, MobileServicingEntryInput input, CancellationToken ct = default)
    {
        if (input.TotalAmount < 0)
        {
            throw new ValidationException("Total servicing amount cannot be negative.");
        }

        if (input.TotalCost < 0)
        {
            throw new ValidationException("Total cost cannot be negative.");
        }

        if (input.TotalCost > input.TotalAmount)
        {
            throw new ValidationException("Total material/parts cost cannot exceed total servicing amount.");
        }

        if (input.CommissionRate < 0 || input.CommissionRate > 100)
        {
            throw new ValidationException("Commission rate must be between 0% and 100%.");
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppPolicies.MakeSales, ct);

        var technician = await db.Technicians.FirstOrDefaultAsync(t => t.Id == input.TechnicianId && !t.IsDeleted, ct)
            ?? throw new ValidationException("Selected technician does not exist.");

        var activeDate = input.Date != default ? input.Date : await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);
        var locationId = input.LocationId;
        if (locationId == 0)
        {
            var userLocation = await db.Users
                .Where(u => u.Id == actorId)
                .Select(u => u.CashLocationId)
                .SingleOrDefaultAsync(ct);
            locationId = userLocation ?? (await db.StockLocations.Where(l => l.CanSell && !l.IsDeleted).Select(l => l.Id).FirstOrDefaultAsync(ct));
        }

        var profit = Math.Max(0m, input.TotalAmount - input.TotalCost);
        var techCut = Math.Round(profit * (input.CommissionRate / 100m), 2, MidpointRounding.AwayFromZero);
        var shopProfit = profit - techCut;

        MobileServicingEntry entry;
        if (input.Id is { } id and > 0)
        {
            entry = await db.MobileServicingEntries.FirstOrDefaultAsync(e => e.Id == id, ct)
                ?? throw new ValidationException("Servicing entry not found.");

            if (entry.IsDeposited)
            {
                throw new ValidationException("This servicing entry has already been deposited into the cash drawer and cannot be modified.");
            }

            entry.TotalAmount = input.TotalAmount;
            entry.TotalCost = input.TotalCost;
            entry.CommissionRate = input.CommissionRate;
            entry.TechnicianCut = techCut;
            entry.ShopProfit = shopProfit;
            entry.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            entry.UpdatedByUserId = actorId;
        }
        else
        {
            // Check if an undeposited entry for the same (date, location, technician) already exists
            var existing = await db.MobileServicingEntries
                .FirstOrDefaultAsync(e => e.Date == activeDate && e.LocationId == locationId && e.TechnicianId == input.TechnicianId, ct);

            if (existing != null)
            {
                if (existing.IsDeposited)
                {
                    throw new ValidationException("A servicing entry for this technician today has already been deposited into the cash drawer.");
                }

                // Update existing record
                existing.TotalAmount = input.TotalAmount;
                existing.TotalCost = input.TotalCost;
                existing.CommissionRate = input.CommissionRate;
                existing.TechnicianCut = techCut;
                existing.ShopProfit = shopProfit;
                existing.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                existing.UpdatedByUserId = actorId;
                entry = existing;
            }
            else
            {
                entry = new MobileServicingEntry
                {
                    Date = activeDate,
                    LocationId = locationId,
                    TechnicianId = input.TechnicianId,
                    TotalAmount = input.TotalAmount,
                    TotalCost = input.TotalCost,
                    CommissionRate = input.CommissionRate,
                    TechnicianCut = techCut,
                    ShopProfit = shopProfit,
                    Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByUserId = actorId
                };
                db.MobileServicingEntries.Add(entry);
            }
        }

        await db.SaveChangesAsync(ct);
        return entry.Id;
    }

    public async Task DeleteMobileServicingEntryAsync(ClaimsPrincipal actor, int entryId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppPolicies.MakeSales, ct);

        var entry = await db.MobileServicingEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new ValidationException("Servicing entry not found.");

        if (entry.IsDeposited)
        {
            throw new ValidationException("Cannot delete a servicing entry that has already been deposited into the cash drawer.");
        }

        db.MobileServicingEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(decimal DepositedAmount, int EntriesCount)> DepositServicingProfitAsync(
        ClaimsPrincipal actor,
        DateOnly date,
        int locationId,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppPolicies.MakeSales, ct);

        var undepositedEntries = await db.MobileServicingEntries
            .Where(e => e.Date == date && e.LocationId == locationId && !e.IsDeposited)
            .ToListAsync(ct);

        if (undepositedEntries.Count == 0)
        {
            throw new ValidationException("No undeposited servicing profit found for this date and location.");
        }

        var totalShopProfit = undepositedEntries.Sum(e => e.ShopProfit);
        if (totalShopProfit <= 0)
        {
            foreach (var entry in undepositedEntries)
            {
                entry.IsDeposited = true;
                entry.DepositedAt = DateTimeOffset.UtcNow;
                entry.UpdatedByUserId = actorId;
                entry.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return (0m, undepositedEntries.Count);
        }

        var session = await CashDrawerService.RequireOpenAsync(db, actorId, ct, locationId);

        var movement = new CashMovement
        {
            RequestId = Guid.NewGuid(),
            CashSessionId = session.Id,
            BusinessDate = date,
            Kind = CashMovementKind.ServicingProfit,
            Amount = totalShopProfit,
            Notes = $"Mobile servicing profit deposit for {date:yyyy-MM-dd} ({undepositedEntries.Count} technicians)",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = actorId
        };

        CashDrawerService.Append(db, session, movement);

        foreach (var entry in undepositedEntries)
        {
            entry.IsDeposited = true;
            entry.DepositedAt = DateTimeOffset.UtcNow;
            entry.CashMovement = movement;
            entry.UpdatedByUserId = actorId;
            entry.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return (totalShopProfit, undepositedEntries.Count);
    }

    public async Task<decimal> GetUndepositedServicingProfitAsync(DateOnly date, int locationId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.MobileServicingEntries
            .Where(e => e.Date == date && e.LocationId == locationId && !e.IsDeposited)
            .SumAsync(e => (decimal?)e.ShopProfit, ct) ?? 0m;
    }
}
