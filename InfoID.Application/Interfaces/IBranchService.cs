using InfoID.Application.Dtos.Organization;
using InfoID.Shared.Common;

namespace InfoID.Application.Interfaces;

public interface IBranchService
{
    Task<BranchDto?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<BranchDto>> GetByOrganizationAsync(long organizationId, CancellationToken ct = default);

    Task<Result<long>> CreateAsync(CreateBranchRequest request, CancellationToken ct = default);

    Task<Result> UpdateAsync(UpdateBranchRequest request, CancellationToken ct = default);

    Task<Result> SoftDeleteAsync(long id, CancellationToken ct = default);
}
