using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Desktop.Features.BlankCard.Models;
using InfoID.Desktop.Features.CardDesigner.Models;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Domain.Common.Interfaces;
using InfoID.Domain.Entities.OrganizationModule;
using InfoID.Domain.Entities.TemplateModule;

namespace InfoID.Desktop.Features.CardDesigner.Services;

public sealed class CardDesignRepository : ICardDesignRepository
{
    private readonly IUnitOfWork _unitOfWork;

    public CardDesignRepository(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<long> SaveAsync(CardDesignDocument document, CancellationToken ct = default)
    {
        var templates = _unitOfWork.Repository<Template>();

        Template? entity = document.PersistedTemplateId is { } id
            ? await templates.GetByIdAsync(id, ct)
            : null;

        var isNew = entity is null;
        if (isNew)
        {
            entity = new Template
            {
                OrganizationId = await EnsureDefaultOrganizationAsync(ct),
                CreatedDate = DateTime.UtcNow,
                LastOpenedDate = DateTime.UtcNow,
            };
        }

        entity!.Name = document.Name;
        entity.CardSize = document.CardFormatId;
        entity.FrontDesignJson = JsonSerializer.Serialize(document.Front);
        entity.BackDesignJson = JsonSerializer.Serialize(document.Back);
        entity.DocumentMetadataJson = JsonSerializer.Serialize(BuildMetadata(document));
        entity.CurrentVersionNumber = document.VersionNumber;
        entity.ModifiedDate = DateTime.UtcNow;

        if (isNew)
        {
            await templates.AddAsync(entity, ct);
        }
        else
        {
            templates.Update(entity);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        document.PersistedTemplateId = entity.Id;
        return entity.Id;
    }

    public async Task<CardDesignDocument?> LoadAsync(long templateId, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.Repository<Template>().GetByIdAsync(templateId, ct);
        return entity is null ? null : MapToDocument(entity);
    }

    public async Task<IReadOnlyList<RecentDesignSummary>> GetRecentAsync(int maxCount = 20, CancellationToken ct = default)
    {
        var all = await _unitOfWork.Repository<Template>().GetAllAsync(ct);

        return all
            .Where(t => t.LastOpenedDate is not null)
            .OrderByDescending(t => t.IsPinnedRecent)
            .ThenByDescending(t => t.LastOpenedDate)
            .Take(maxCount)
            .Select(t => new RecentDesignSummary(t.Id, t.Name, t.CardSize, t.LastOpenedDate, t.ModifiedDate ?? t.CreatedDate, t.IsPinnedRecent))
            .ToList();
    }

    public async Task MarkOpenedAsync(long templateId, CancellationToken ct = default)
    {
        var repo = _unitOfWork.Repository<Template>();
        var entity = await repo.GetByIdAsync(templateId, ct);
        if (entity is null) return;

        entity.LastOpenedDate = DateTime.UtcNow;
        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task SetPinnedAsync(long templateId, bool pinned, CancellationToken ct = default)
    {
        var repo = _unitOfWork.Repository<Template>();
        var entity = await repo.GetByIdAsync(templateId, ct);
        if (entity is null) return;

        entity.IsPinnedRecent = pinned;
        repo.Update(entity);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(long templateId, CancellationToken ct = default)
    {
        var repo = _unitOfWork.Repository<Template>();
        var entity = await repo.GetByIdAsync(templateId, ct);
        if (entity is null) return;

        repo.SoftDelete(entity);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task SaveVersionSnapshotAsync(long templateId, CardDesignDocument document, string? changeNote, CancellationToken ct = default)
    {
        var versions = _unitOfWork.Repository<TemplateVersion>();

        await versions.AddAsync(new TemplateVersion
        {
            TemplateId = templateId,
            VersionNumber = document.VersionNumber,
            SnapshotJson = JsonSerializer.Serialize(document),
            ChangeNote = changeNote,
            CreatedDate = DateTime.UtcNow,
        }, ct);

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TemplateVersionSummary>> GetVersionsAsync(long templateId, CancellationToken ct = default)
    {
        var versions = await _unitOfWork.Repository<TemplateVersion>().FindAsync(v => v.TemplateId == templateId, ct);

        return versions
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new TemplateVersionSummary(v.Id, v.VersionNumber, v.CreatedDate, v.ChangeNote))
            .ToList();
    }

    public async Task<CardDesignDocument?> GetVersionSnapshotAsync(long versionId, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.Repository<TemplateVersion>().GetByIdAsync(versionId, ct);
        if (entity is null || string.IsNullOrEmpty(entity.SnapshotJson)) return null;

        try
        {
            return JsonSerializer.Deserialize<CardDesignDocument>(entity.SnapshotJson);
        }
        catch
        {
            // A corrupt/unreadable snapshot must not crash the Version History dialog --
            // the caller treats a null result as "this version can't be restored".
            return null;
        }
    }

    /// <summary>InfoID is single-user/single-machine (SRS 3.3), but Template.OrganizationId
    /// is a required FK -- so the first save bootstraps one placeholder Organization row.
    /// Real activation/licensing (Part 4.7 of the SRS) will populate this properly; this
    /// is just enough to satisfy the FK until that lands.</summary>
    private async Task<long> EnsureDefaultOrganizationAsync(CancellationToken ct)
    {
        var orgs = _unitOfWork.Repository<Organization>();
        var existing = await orgs.GetAllAsync(ct);
        if (existing.Count > 0) return existing[0].Id;

        var org = new Organization
        {
            Name = "My Organization",
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow,
        };

        await orgs.AddAsync(org, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return org.Id;
    }

    private static object BuildMetadata(CardDesignDocument document) => new
    {
        document.SchemaVersion,
        document.WidthMm,
        document.HeightMm,
        Orientation = document.Orientation.ToString(),
        document.CornerRadiusMm,
        document.BleedMm,
        document.SafeZoneMm,
        document.ShowGrid,
        document.SnapToGrid,
        document.GridSizeMm,
        document.Metadata,
    };

    private static CardDesignDocument MapToDocument(Template entity)
    {
        var document = new CardDesignDocument
        {
            Name = entity.Name,
            CardFormatId = entity.CardSize,
            VersionNumber = entity.CurrentVersionNumber,
            PersistedTemplateId = entity.Id,
        };

        if (!string.IsNullOrEmpty(entity.DocumentMetadataJson))
        {
            using var parsed = JsonDocument.Parse(entity.DocumentMetadataJson);
            var root = parsed.RootElement;

            if (root.TryGetProperty("SchemaVersion", out var sv)) document.SchemaVersion = sv.GetInt32();
            if (root.TryGetProperty("WidthMm", out var w)) document.WidthMm = w.GetDouble();
            if (root.TryGetProperty("HeightMm", out var h)) document.HeightMm = h.GetDouble();
            if (root.TryGetProperty("Orientation", out var o) && Enum.TryParse<CardOrientation>(o.GetString(), out var orientation)) document.Orientation = orientation;
            if (root.TryGetProperty("CornerRadiusMm", out var cr)) document.CornerRadiusMm = cr.GetDouble();
            if (root.TryGetProperty("BleedMm", out var bl)) document.BleedMm = bl.GetDouble();
            if (root.TryGetProperty("SafeZoneMm", out var sz)) document.SafeZoneMm = sz.GetDouble();
            if (root.TryGetProperty("ShowGrid", out var sg)) document.ShowGrid = sg.GetBoolean();
            if (root.TryGetProperty("SnapToGrid", out var stg)) document.SnapToGrid = stg.GetBoolean();
            if (root.TryGetProperty("GridSizeMm", out var gs)) document.GridSizeMm = gs.GetDouble();
        }

        if (!string.IsNullOrEmpty(entity.FrontDesignJson))
        {
            document.Front = JsonSerializer.Deserialize<CardDesignSide>(entity.FrontDesignJson) ?? document.Front;
        }

        if (!string.IsNullOrEmpty(entity.BackDesignJson))
        {
            document.Back = JsonSerializer.Deserialize<CardDesignSide>(entity.BackDesignJson) ?? document.Back;
        }

        return document;
    }
}