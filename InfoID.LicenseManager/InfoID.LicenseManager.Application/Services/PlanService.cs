using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Plans;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Domain.Exceptions;
using InfoID.LicenseManager.Domain.ValueObjects;
using InfoID.Shared.Common;

namespace InfoID.LicenseManager.Application.Services;

public sealed class PlanService : IPlanService
{
    private readonly IPlanRepository _plans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;

    public PlanService(IPlanRepository plans, IUnitOfWork unitOfWork, IAuditService audit, ICurrentUser currentUser)
    {
        _plans = plans;
        _unitOfWork = unitOfWork;
        _audit = audit;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<PlanDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.RequireStaff("view plans");
        return (await _plans.GetAllAsync(ProductCodes.InfoID, cancellationToken)).Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<PlanDto>> GetActivePlansAsync(string productCode, CancellationToken cancellationToken = default) =>
        (await _plans.GetActiveAsync(productCode, cancellationToken)).Select(ToDto).ToList();

    public async Task<PlanDto> GetAsync(long planId, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireStaff("view plans");
        var plan = await _plans.FindByIdAsync(planId, cancellationToken);
        return plan is null || plan.Cancelled ? throw new NotFoundException("Plan", planId) : ToDto(plan);
    }

    public async Task<Result<PlanDto>> CreateAsync(PlanInputDto input, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireManageLicenses("edit plans");

        Plan plan;
        try
        {
            plan = Plan.Create(ProductCodes.InfoID, ToDetails(input));
        }
        catch (DomainRuleException ex)
        {
            return Result<PlanDto>.Failure(ex.Message);
        }

        _plans.Add(plan);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Plan.Created", new { plan.Id, plan.Name, plan.Tier, plan.IsActive }, cancellationToken: cancellationToken);
        return Result<PlanDto>.Success(ToDto(plan));
    }

    public async Task<Result<PlanDto>> UpdateAsync(UpdatePlanRequestDto request, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireManageLicenses("edit plans");

        var plan = await _plans.FindByIdAsync(request.PlanId, cancellationToken);
        if (plan is null || plan.Cancelled) throw new NotFoundException("Plan", request.PlanId);

        try
        {
            plan.Update(ToDetails(request.Plan));
        }
        catch (DomainRuleException ex)
        {
            return Result<PlanDto>.Failure(ex.Message);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Plan.Updated", new { plan.Id, plan.Name, plan.Tier, plan.IsActive }, cancellationToken: cancellationToken);
        return Result<PlanDto>.Success(ToDto(plan));
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        _unitOfWork.UseActor("Setup");

        if (!await _plans.ProductExistsAsync(ProductCodes.InfoID, cancellationToken))
            _plans.AddProduct(new Product { Code = ProductCodes.InfoID, Name = "InfoID" });

        if (!await _plans.AnyPlansAsync(ProductCodes.InfoID, cancellationToken))
            _plans.AddRange(DefaultPlans());

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The three editions of SRS section 3.3, as the desktop manager seeded them. Staff edit them afterwards.</summary>
    private static IEnumerable<Plan> DefaultPlans() =>
    [
        Default("Standard", LicenseTier.Standard, 1,
            "Small organizations. Design and print cards from Excel/CSV data.",
            "Card designer (all card sizes)\nText, images, shapes, date/time\nPhoto and signature capture\nExcel and CSV data import\nSingle and batch printing\nDigital copy (PDF)\nBasic reports"),
        Default("Professional", LicenseTier.Professional, 2,
            "Schools and companies. Databases, barcodes and RFID.",
            "Everything in Standard\nQR codes and barcodes\nDatabase import (SQL Server, MySQL, PostgreSQL, Oracle, Access)\nRFID encoding (MIFARE Classic)\nPrint queue\nPrint cost tracking"),
        Default("Enterprise", LicenseTier.Enterprise, 3,
            "Large organizations. Security, integration and automation.",
            "Everything in Professional\nRFID: DESFire, HID iCLASS/Prox, NFC\nAccess control integration\nAdvanced reports\nAI-assisted features\nREST API and plugins\nCloud backup"),
    ];

    private static Plan Default(string name, LicenseTier tier, int order, string tagline, string features) =>
        Plan.Create(ProductCodes.InfoID, new PlanDetails(name, tier, tagline, features, 12, 1, null, CardLimitPeriod.Monthly, "Contact sales", order, true));

    private static PlanDetails ToDetails(PlanInputDto p) => new(
        p.Name, p.Tier, p.Tagline, p.FeaturesText, p.DurationMonths, p.DefaultMachines, p.MaxCardsPerPeriod, p.LimitPeriod, p.PriceText, p.SortOrder, p.IsActive);

    private static PlanDto ToDto(Plan p) => new(
        p.Id, p.Name, p.Tier, p.Tagline, p.FeaturesText, p.DurationMonths, p.DefaultMachines, p.MaxCardsPerPeriod,
        p.CardLimitPeriod, p.PriceText, p.SortOrder, p.IsActive);
}
