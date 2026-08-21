using InfoID.Application.Dtos.Organization;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Interfaces;
using InfoID.Shared.Common;
using OrgEntity = InfoID.Domain.Entities.OrganizationModule.Organization;

namespace InfoID.Application.Services;

public class OrganizationService : IOrganizationService
{
    private readonly IUnitOfWork _uow;

    public OrganizationService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<OrganizationDto?> GetActiveOrganizationAsync(CancellationToken ct = default)
    {
        var all = await _uow.Repository<OrgEntity>().GetAllAsync(ct);
        var org = all.OrderBy(o => o.CreatedDate).FirstOrDefault();
        return org is null ? null : ToDto(org);
    }

    public async Task<OrganizationDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var org = await _uow.Repository<OrgEntity>().GetByIdAsync(id, ct);
        return org is null ? null : ToDto(org);
    }

    public async Task<Result<long>> CreateAsync(CreateOrganizationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<long>.Failure("Organization name is required.");
        }

        var entity = new OrgEntity
        {
            Name = request.Name.Trim(),
            OrgType = request.OrgType,
            Email = request.Email,
            Phone = request.Phone,
            Country = request.Country,
            Address = request.Address,
        };

        await _uow.Repository<OrgEntity>().AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<long>.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(UpdateOrganizationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure("Organization name is required.");
        }

        var entity = await _uow.Repository<OrgEntity>().GetByIdAsync(request.Id, ct);
        if (entity is null)
        {
            return Result.Failure($"Organization {request.Id} was not found.");
        }

        entity.Name = request.Name.Trim();
        entity.OrgType = request.OrgType;
        entity.Email = request.Email;
        entity.Phone = request.Phone;
        entity.Country = request.Country;
        entity.Address = request.Address;
        entity.LogoPath = request.LogoPath;

        // AccentColorHex has no UI control anymore (organization branding is logo-only
        // now) -- only touch it if a caller explicitly supplies a value, so a request
        // built without that field never silently wipes out whatever is already stored.
        if (request.AccentColorHex is not null)
        {
            entity.AccentColorHex = request.AccentColorHex;
        }

        _uow.Repository<OrgEntity>().Update(entity);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static OrganizationDto ToDto(OrgEntity o) => new()
    {
        Id = o.Id,
        Name = o.Name,
        OrgType = o.OrgType,
        Email = o.Email,
        Phone = o.Phone,
        Country = o.Country,
        Address = o.Address,
        LogoPath = o.LogoPath,
        AccentColorHex = o.AccentColorHex,
        CreatedDate = o.CreatedDate,
    };
}
