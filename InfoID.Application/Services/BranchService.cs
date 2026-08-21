using InfoID.Application.Dtos.Organization;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Interfaces;
using InfoID.Shared.Common;
using BranchEntity = InfoID.Domain.Entities.OrganizationModule.Branch;

namespace InfoID.Application.Services;

public class BranchService : IBranchService
{
    private readonly IUnitOfWork _uow;

    public BranchService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<BranchDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var branch = await _uow.Repository<BranchEntity>().GetByIdAsync(id, ct);
        return branch is null ? null : ToDto(branch);
    }

    public async Task<IReadOnlyList<BranchDto>> GetByOrganizationAsync(long organizationId, CancellationToken ct = default)
    {
        var branches = await _uow.Repository<BranchEntity>().FindAsync(b => b.OrganizationId == organizationId, ct);
        return branches.Select(ToDto).ToList();
    }

    public async Task<Result<long>> CreateAsync(CreateBranchRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<long>.Failure("Branch name is required.");
        }

        var entity = new BranchEntity
        {
            OrganizationId = request.OrganizationId,
            Name = request.Name.Trim(),
            Code = request.Code,
            Address = request.Address,
            NumberingPrefix = request.NumberingPrefix,
        };

        await _uow.Repository<BranchEntity>().AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<long>.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(UpdateBranchRequest request, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<BranchEntity>().GetByIdAsync(request.Id, ct);
        if (entity is null)
        {
            return Result.Failure($"Branch {request.Id} was not found.");
        }

        entity.Name = request.Name.Trim();
        entity.Code = request.Code;
        entity.Address = request.Address;
        entity.NumberingPrefix = request.NumberingPrefix;
        entity.DefaultPrinterProfileId = request.DefaultPrinterProfileId;
        entity.DefaultTemplateId = request.DefaultTemplateId;

        _uow.Repository<BranchEntity>().Update(entity);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> SoftDeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<BranchEntity>().GetByIdAsync(id, ct);
        if (entity is null)
        {
            return Result.Failure($"Branch {id} was not found.");
        }

        _uow.Repository<BranchEntity>().SoftDelete(entity);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static BranchDto ToDto(BranchEntity b) => new()
    {
        Id = b.Id,
        OrganizationId = b.OrganizationId,
        Name = b.Name,
        Code = b.Code,
        Address = b.Address,
        NumberingPrefix = b.NumberingPrefix,
        DefaultPrinterProfileId = b.DefaultPrinterProfileId,
        DefaultTemplateId = b.DefaultTemplateId,
    };
}
