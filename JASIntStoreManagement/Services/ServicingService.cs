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
                t.ServicingJobs.Count,
                t.ServicingJobs.Sum(j => (decimal?)j.ShopProfit + j.TechnicianCut) ?? 0m,
                t.ServicingJobs.Sum(j => (decimal?)j.TechnicianCut) ?? 0m))
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

    public async Task<ServicingPageData> GetServicingPageDataAsync(
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
                t.ServicingJobs.Count,
                t.ServicingJobs.Sum(j => (decimal?)j.ShopProfit + j.TechnicianCut) ?? 0m,
                t.ServicingJobs.Sum(j => (decimal?)j.TechnicianCut) ?? 0m))
            .ToListAsync(ct);

        var jobsQuery = db.ServicingJobs
            .AsNoTracking()
            .Include(j => j.Location)
            .Include(j => j.Technician)
            .Where(j => j.Date == activeDate);

        if (selectedLocationId.HasValue)
        {
            jobsQuery = jobsQuery.Where(j => j.LocationId == selectedLocationId.Value);
        }

        var jobs = await jobsQuery
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new ServicingJobDto(
                j.Id,
                j.Date,
                j.LocationId,
                j.Location != null ? j.Location.Name : "Unknown",
                j.TechnicianId,
                j.Technician != null ? j.Technician.Name : "Unknown",
                j.GadgetDescription,
                j.CustomerName,
                j.CustomerPhone,
                j.BillAmount,
                j.MaterialCost,
                j.CommissionRate,
                j.BillAmount - j.MaterialCost,
                j.TechnicianCut,
                j.ShopProfit,
                j.IsDeposited,
                j.DepositedAt,
                j.CreatedAt,
                j.Notes))
            .ToListAsync(ct);

        var totalJobs = jobs.Count;
        var totalBilled = jobs.Sum(j => j.BillAmount);
        var totalMaterial = jobs.Sum(j => j.MaterialCost);
        var totalProfit = jobs.Sum(j => j.JobProfit);
        var totalTechCut = jobs.Sum(j => j.TechnicianCut);
        var totalShopProfit = jobs.Sum(j => j.ShopProfit);
        var undeposited = jobs.Where(j => !j.IsDeposited).Sum(j => j.ShopProfit);
        var deposited = jobs.Where(j => j.IsDeposited).Sum(j => j.ShopProfit);

        var summary = new ServicingSummaryDto(
            totalJobs,
            totalBilled,
            totalMaterial,
            totalProfit,
            totalTechCut,
            totalShopProfit,
            undeposited,
            deposited);

        var hasActiveDrawer = false;
        var drawerBalance = 0m;

        if (selectedLocationId.HasValue)
        {
            var drawer = await db.CashDrawers
                .Include(d => d.CurrentSession)
                .FirstOrDefaultAsync(d => d.StockLocationId == selectedLocationId.Value, ct);

            if (drawer?.CurrentSession is { ClosedAt: null } session)
            {
                hasActiveDrawer = true;
                drawerBalance = drawer.CurrentCash;
            }
        }

        return new ServicingPageData(
            activeDate,
            selectedLocationId,
            locations,
            technicians,
            jobs,
            summary,
            hasActiveDrawer,
            drawerBalance);
    }

    public async Task<int> CreateServicingJobAsync(ClaimsPrincipal actor, ServicingJobInput input, CancellationToken ct = default)
    {
        if (input.BillAmount <= 0)
        {
            throw new ValidationException("Customer bill amount must be greater than zero.");
        }

        if (input.MaterialCost < 0)
        {
            throw new ValidationException("Material cost cannot be negative.");
        }

        if (input.MaterialCost > input.BillAmount)
        {
            throw new ValidationException("Material cost cannot exceed the customer bill amount.");
        }

        if (string.IsNullOrWhiteSpace(input.GadgetDescription))
        {
            throw new ValidationException("Gadget / service description is required.");
        }

        if (input.CommissionRate < 0 || input.CommissionRate > 100)
        {
            throw new ValidationException("Commission rate must be between 0% and 100%.");
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppPolicies.MakeSales, ct);

        var technician = await db.Technicians.FirstOrDefaultAsync(t => t.Id == input.TechnicianId && !t.IsDeleted, ct)
            ?? throw new ValidationException("Selected technician was not found.");

        var location = await db.StockLocations.FirstOrDefaultAsync(l => l.Id == input.LocationId && l.CanSell && !l.IsDeleted, ct)
            ?? throw new ValidationException("Selected store location was not found or cannot sell.");

        var activeDate = input.Date != default ? input.Date : await AccountingCalendar.SelectedAsync(db, actorId, configuration, ct);

        var profit = input.BillAmount - input.MaterialCost;
        var techCut = Math.Round(profit * (input.CommissionRate / 100m), 2, MidpointRounding.AwayFromZero);
        var shopProfit = profit - techCut;

        var job = new ServicingJob
        {
            Date = activeDate,
            LocationId = location.Id,
            TechnicianId = technician.Id,
            GadgetDescription = input.GadgetDescription.Trim(),
            CustomerName = string.IsNullOrWhiteSpace(input.CustomerName) ? null : input.CustomerName.Trim(),
            CustomerPhone = string.IsNullOrWhiteSpace(input.CustomerPhone) ? null : input.CustomerPhone.Trim(),
            BillAmount = input.BillAmount,
            MaterialCost = input.MaterialCost,
            CommissionRate = input.CommissionRate,
            TechnicianCut = techCut,
            ShopProfit = shopProfit,
            IsDeposited = false,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = actorId
        };

        db.ServicingJobs.Add(job);
        await db.SaveChangesAsync(ct);

        return job.Id;
    }

    public async Task DeleteServicingJobAsync(ClaimsPrincipal actor, int jobId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppPolicies.MakeSales, ct);

        var job = await db.ServicingJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new ValidationException("Servicing job was not found.");

        if (job.IsDeposited)
        {
            throw new ValidationException("This job's profit has already been deposited into the cash drawer. Cannot delete deposited jobs.");
        }

        db.ServicingJobs.Remove(job);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(decimal DepositedAmount, int JobsCount)> DepositServicingProfitAsync(
        ClaimsPrincipal actor,
        DateOnly date,
        int locationId,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actorId = await StoreAccess.RequireAsync(db, actor, AppPolicies.MakeSales, ct);

        var undepositedJobs = await db.ServicingJobs
            .Where(j => j.Date == date && j.LocationId == locationId && !j.IsDeposited)
            .ToListAsync(ct);

        if (undepositedJobs.Count == 0)
        {
            throw new ValidationException("No undeposited servicing profit found for this date and location.");
        }

        var totalShopProfit = undepositedJobs.Sum(j => j.ShopProfit);
        if (totalShopProfit <= 0)
        {
            // If total profit is zero (e.g. at cost), mark them as deposited without cash movement
            foreach (var job in undepositedJobs)
            {
                job.IsDeposited = true;
                job.DepositedAt = DateTimeOffset.UtcNow;
                job.UpdatedByUserId = actorId;
                job.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return (0m, undepositedJobs.Count);
        }

        // Require active open cash drawer session for the location
        var session = await CashDrawerService.RequireOpenAsync(db, actorId, ct, locationId);

        var movement = new CashMovement
        {
            RequestId = Guid.NewGuid(),
            CashSessionId = session.Id,
            BusinessDate = date,
            Kind = CashMovementKind.ServicingProfit,
            Amount = totalShopProfit,
            Notes = $"Servicing profit deposit for {date:yyyy-MM-dd} ({undepositedJobs.Count} jobs)",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = actorId
        };

        CashDrawerService.Append(db, session, movement);

        foreach (var job in undepositedJobs)
        {
            job.IsDeposited = true;
            job.DepositedAt = DateTimeOffset.UtcNow;
            job.CashMovement = movement;
            job.UpdatedByUserId = actorId;
            job.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return (totalShopProfit, undepositedJobs.Count);
    }

    public async Task<decimal> GetUndepositedServicingProfitAsync(DateOnly date, int locationId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ServicingJobs
            .Where(j => j.Date == date && j.LocationId == locationId && !j.IsDeposited)
            .SumAsync(j => (decimal?)j.ShopProfit, ct) ?? 0m;
    }
}
