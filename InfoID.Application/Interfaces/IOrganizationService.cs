using InfoID.Application.Dtos.Organization;
using InfoID.Shared.Common;

namespace InfoID.Application.Interfaces;

public interface IOrganizationService
{
    /// <summary>InfoID is effectively single-organization per install (see the
    /// Organization module notes -- one row normally, more only across
    /// re-activation history), so this is the common entry point rather than
    /// a generic GetAllAsync.</summary>
    Task<OrganizationDto?> GetActiveOrganizationAsync(CancellationToken ct = default);

    Task<OrganizationDto?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<Result<long>> CreateAsync(CreateOrganizationRequest request, CancellationToken ct = default);

    Task<Result> UpdateAsync(UpdateOrganizationRequest request, CancellationToken ct = default);
}
