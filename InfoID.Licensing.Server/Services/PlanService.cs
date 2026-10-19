using InfoID.Licensing.Core;
using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Licensing.Server.Services;

public sealed record PlanEdit(
    long? Id,
    string Name,
    LicenseTier Tier,
    string? Tagline,
    string? FeaturesText,
    int? DurationMonths,
    int DefaultMachines,
    int? MaxCardsPerPeriod,
    CardLimitPeriod LimitPeriod,
    string? PriceText,
    int SortOrder,
    bool IsActive);

/// <summary>Plans shown on the app's Editions page, managed in License Manager, so
/// plans can change without releasing a new app version.</summary>
public sealed class PlanService
{
    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;
    private readonly AdminContext _admin;
    private readonly AuditService _audit;

    public PlanService(IDbContextFactory<LicenseDbContext> dbFactory, AdminContext admin, AuditService audit)
    {
        _dbFactory = dbFactory;
        _admin = admin;
        _audit = audit;
    }

    /// <summary>Active plans for the app (public).</summary>
    public async Task<IReadOnlyList<Plan>> GetActivePlansAsync(string productCode = ProductCodes.InfoID)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Plans.AsNoTracking()
            .Where(p => p.ProductCode == productCode && p.IsActive && !p.Cancelled)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync();
    }

    /// <summary>All plans (License Manager).</summary>
    public async Task<IReadOnlyList<Plan>> GetAllAsync(string productCode = ProductCodes.InfoID)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Plans.AsNoTracking()
            .Where(p => p.ProductCode == productCode && !p.Cancelled)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync();
    }

    public async Task<Plan> SaveAsync(PlanEdit edit, string productCode = ProductCodes.InfoID)
    {
        _admin.Require(_admin.CanManageLicenses, "edit plans");
        if (string.IsNullOrWhiteSpace(edit.Name)) throw new ArgumentException("Enter a plan name.");
        if (edit.DurationMonths is < 1 or > 120) throw new ArgumentException("Duration must be 1-120 months, or empty for perpetual.");
        if (edit.DefaultMachines is < 1 or > 100) throw new ArgumentException("Machines must be between 1 and 100.");
        if (edit.MaxCardsPerPeriod is < 1) throw new ArgumentException("The card limit must be at least 1, or empty for unlimited.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = _admin.Email;
        var plan = edit.Id is { } id ? await db.Plans.FirstAsync(p => p.Id == id) : new Plan { ProductCode = productCode };
        if (edit.Id is null) db.Plans.Add(plan);

        plan.Name = edit.Name.Trim();
        plan.Tier = edit.Tier.ToString();
        plan.Tagline = Clean(edit.Tagline);
        plan.FeaturesText = Clean(edit.FeaturesText);
        plan.DurationMonths = edit.DurationMonths;
        plan.DefaultMachines = edit.DefaultMachines;
        plan.MaxCardsPerPeriod = edit.MaxCardsPerPeriod;
        plan.CardLimitPeriod = edit.LimitPeriod.ToString();
        plan.PriceText = Clean(edit.PriceText);
        plan.SortOrder = edit.SortOrder;
        plan.IsActive = edit.IsActive;
        await db.SaveChangesAsync();

        await _audit.LogAsync(edit.Id is null ? "Plan.Created" : "Plan.Updated", new { plan.Id, plan.Name, plan.Tier, plan.IsActive });
        return plan;
    }

    /// <summary>First run: the InfoID product and the three SRS editions (section 3.3),
    /// so the Editions page has content immediately. Staff edit them afterwards.</summary>
    public async Task EnsureDefaultsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CurrentUser = "Setup";

        if (!await db.Products.AnyAsync(p => p.Code == ProductCodes.InfoID))
            db.Products.Add(new Product { Code = ProductCodes.InfoID, Name = "InfoID" });

        if (!await db.Plans.AnyAsync(p => p.ProductCode == ProductCodes.InfoID))
        {
            db.Plans.AddRange(
                new Plan
                {
                    Name = "Standard", Tier = nameof(LicenseTier.Standard), SortOrder = 1, DurationMonths = 12,
                    Tagline = "Small organizations. Design and print cards from Excel/CSV data.",
                    FeaturesText = "Card designer (all card sizes)\nText, images, shapes, date/time\nPhoto and signature capture\nExcel and CSV data import\nSingle and batch printing\nDigital copy (PDF)\nBasic reports",
                    PriceText = "Contact sales",
                },
                new Plan
                {
                    Name = "Professional", Tier = nameof(LicenseTier.Professional), SortOrder = 2, DurationMonths = 12,
                    Tagline = "Schools and companies. Databases, barcodes and RFID.",
                    FeaturesText = "Everything in Standard\nQR codes and barcodes\nDatabase import (SQL Server, MySQL, PostgreSQL, Oracle, Access)\nRFID encoding (MIFARE Classic)\nPrint queue\nPrint cost tracking",
                    PriceText = "Contact sales",
                },
                new Plan
                {
                    Name = "Enterprise", Tier = nameof(LicenseTier.Enterprise), SortOrder = 3, DurationMonths = 12,
                    Tagline = "Large organizations. Security, integration and automation.",
                    FeaturesText = "Everything in Professional\nRFID: DESFire, HID iCLASS/Prox, NFC\nAccess control integration\nAdvanced reports\nAI-assisted features\nREST API and plugins\nCloud backup",
                    PriceText = "Contact sales",
                });
        }

        await db.SaveChangesAsync();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
