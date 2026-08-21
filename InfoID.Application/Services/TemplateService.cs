using InfoID.Application.Dtos.TemplateModule;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Interfaces;
using InfoID.Shared.Common;
using TemplateEntity = InfoID.Domain.Entities.TemplateModule.Template;
using TemplateVersionEntity = InfoID.Domain.Entities.TemplateModule.TemplateVersion;

namespace InfoID.Application.Services;

public class TemplateService : ITemplateService
{
    private readonly IUnitOfWork _uow;

    public TemplateService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<TemplateDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<TemplateEntity>().GetByIdAsync(id, ct);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<TemplateSummaryDto>> GetByOrganizationAsync(long organizationId, CancellationToken ct = default)
    {
        var templates = await _uow.Repository<TemplateEntity>().FindAsync(t => t.OrganizationId == organizationId, ct);
        return templates
            .OrderByDescending(t => t.ModifiedDate)
            .Select(ToSummaryDto)
            .ToList();
    }

    public async Task<Result<long>> CreateAsync(CreateTemplateRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<long>.Failure("Template name is required.");
        }

        var entity = new TemplateEntity
        {
            OrganizationId = request.OrganizationId,
            BranchId = request.BranchId,
            Name = request.Name.Trim(),
            CardSize = string.IsNullOrWhiteSpace(request.CardSize) ? "CR-80" : request.CardSize,
            Category = request.Category,
            FrontDesignJson = request.FrontDesignJson,
            BackDesignJson = request.BackDesignJson,
            IsGalleryTemplate = false,
            CurrentVersionNumber = 1,
        };

        await _uow.Repository<TemplateEntity>().AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        await SnapshotVersionAsync(entity, "Initial design", ct);

        return Result<long>.Success(entity.Id);
    }

    public async Task<Result> RenameAsync(RenameTemplateRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure("Template name is required.");
        }

        var entity = await _uow.Repository<TemplateEntity>().GetByIdAsync(request.Id, ct);
        if (entity is null)
        {
            return Result.Failure($"Template {request.Id} was not found.");
        }

        entity.Name = request.Name.Trim();
        _uow.Repository<TemplateEntity>().Update(entity);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> UpdateDesignAsync(UpdateTemplateDesignRequest request, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<TemplateEntity>().GetByIdAsync(request.Id, ct);
        if (entity is null)
        {
            return Result.Failure($"Template {request.Id} was not found.");
        }

        entity.CardSize = string.IsNullOrWhiteSpace(request.CardSize) ? entity.CardSize : request.CardSize;
        entity.FrontDesignJson = request.FrontDesignJson;
        entity.BackDesignJson = request.BackDesignJson;
        entity.CurrentVersionNumber += 1;

        _uow.Repository<TemplateEntity>().Update(entity);
        await _uow.SaveChangesAsync(ct);

        await SnapshotVersionAsync(entity, request.ChangeNote, ct);

        return Result.Success();
    }

    public async Task<Result<long>> DuplicateAsync(long id, string newName, CancellationToken ct = default)
    {
        var source = await _uow.Repository<TemplateEntity>().GetByIdAsync(id, ct);
        if (source is null)
        {
            return Result<long>.Failure($"Template {id} was not found.");
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            return Result<long>.Failure("Template name is required.");
        }

        var clone = new TemplateEntity
        {
            OrganizationId = source.OrganizationId,
            BranchId = source.BranchId,
            Name = newName.Trim(),
            CardSize = source.CardSize,
            // A duplicate of a read-only gallery template becomes a normal, editable
            // working template (FR-DES-10) -- IsGalleryTemplate is never copied.
            Category = source.Category,
            FrontDesignJson = source.FrontDesignJson,
            BackDesignJson = source.BackDesignJson,
            IsGalleryTemplate = false,
            CurrentVersionNumber = 1,
        };

        await _uow.Repository<TemplateEntity>().AddAsync(clone, ct);
        await _uow.SaveChangesAsync(ct);

        await SnapshotVersionAsync(clone, $"Duplicated from '{source.Name}'", ct);

        return Result<long>.Success(clone.Id);
    }

    public async Task<Result> SoftDeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<TemplateEntity>().GetByIdAsync(id, ct);
        if (entity is null)
        {
            return Result.Failure($"Template {id} was not found.");
        }

        _uow.Repository<TemplateEntity>().SoftDelete(entity);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    private async Task SnapshotVersionAsync(TemplateEntity entity, string? changeNote, CancellationToken ct)
    {
        // TemplateVersion.SnapshotJson is documented as "full front+back design at
        // time of save" -- store both sides together as a tiny JSON envelope so a
        // future rollback feature can restore either side from one row.
        var snapshot = $"{{\"front\":{ToJsonOrNull(entity.FrontDesignJson)},\"back\":{ToJsonOrNull(entity.BackDesignJson)}}}";

        await _uow.Repository<TemplateVersionEntity>().AddAsync(new TemplateVersionEntity
        {
            TemplateId = entity.Id,
            VersionNumber = entity.CurrentVersionNumber,
            SnapshotJson = snapshot,
            ChangeNote = changeNote,
        }, ct);

        await _uow.SaveChangesAsync(ct);
    }

    private static string ToJsonOrNull(string? json) => string.IsNullOrWhiteSpace(json) ? "null" : json;

    private static TemplateDto ToDto(TemplateEntity t) => new()
    {
        Id = t.Id,
        OrganizationId = t.OrganizationId,
        BranchId = t.BranchId,
        Name = t.Name,
        CardSize = t.CardSize,
        Category = t.Category,
        FrontDesignJson = t.FrontDesignJson,
        BackDesignJson = t.BackDesignJson,
        IsGalleryTemplate = t.IsGalleryTemplate,
        CurrentVersionNumber = t.CurrentVersionNumber,
        CreatedDate = t.CreatedDate,
        ModifiedDate = t.ModifiedDate ?? t.CreatedDate,
    };

    private static TemplateSummaryDto ToSummaryDto(TemplateEntity t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        CardSize = t.CardSize,
        Category = t.Category,
        IsGalleryTemplate = t.IsGalleryTemplate,
        CurrentVersionNumber = t.CurrentVersionNumber,
        ModifiedDate = t.ModifiedDate ?? t.CreatedDate,
    };
}
