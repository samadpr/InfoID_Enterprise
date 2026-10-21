using InfoID.LicenseManager.Application.DTOs.Plans;
using InfoID.Shared.Common;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>Plans shown on the app Editions page, managed here so plans can change without releasing a new app.</summary>
public interface IPlanService
{
    Task<IReadOnlyList<PlanDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>The plans the app Editions page shows: active ones only. Public (no sign-in), read-only.</summary>
    Task<IReadOnlyList<PlanDto>> GetActivePlansAsync(string productCode, CancellationToken cancellationToken = default);

    /// <exception cref="Common.NotFoundException">No such plan.</exception>
    Task<PlanDto> GetAsync(long planId, CancellationToken cancellationToken = default);

    Task<Result<PlanDto>> CreateAsync(PlanInputDto plan, CancellationToken cancellationToken = default);

    Task<Result<PlanDto>> UpdateAsync(UpdatePlanRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>First run: the InfoID product and the three SRS editions, so the Editions page has content at
    /// once. Idempotent: changes nothing once a product row and plans exist. Not tied to a signed-in user.</summary>
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
}
