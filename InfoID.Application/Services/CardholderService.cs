using InfoID.Application.Dtos.CardholderModule;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Interfaces;
using InfoID.Shared.Common;
using CardholderEntity = InfoID.Domain.Entities.CardholderModule.Cardholder;
using CustomFieldDefinitionEntity = InfoID.Domain.Entities.CardholderModule.CustomFieldDefinition;
using CardholderCustomFieldValueEntity = InfoID.Domain.Entities.CardholderModule.CardholderCustomFieldValue;
using BranchEntity = InfoID.Domain.Entities.OrganizationModule.Branch;

namespace InfoID.Application.Services;

/// <summary>
/// Module E — Cardholder Management. Follows the same
/// UnitOfWork/Repository&lt;T&gt;/Result&lt;T&gt; shape as BranchService and
/// TemplateService: Desktop never touches EF directly, every mutation goes
/// through Result/Result&lt;T&gt; so the UI can show a validation message
/// instead of an unhandled exception, and every delete is a soft-delete
/// (Cancelled = true) per the project's locked-in no-hard-deletes convention.
/// </summary>
public class CardholderService : ICardholderService
{
    private readonly IUnitOfWork _uow;

    public CardholderService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CardholderDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<CardholderEntity>().GetByIdAsync(id, ct);
        if (entity is null) return null;

        var branchName = await ResolveBranchNameAsync(entity.BranchId, ct);
        var values = await _uow.Repository<CardholderCustomFieldValueEntity>()
            .FindAsync(v => v.CardholderId == id, ct);
        var definitions = await _uow.Repository<CustomFieldDefinitionEntity>()
            .FindAsync(d => d.OrganizationId == entity.OrganizationId, ct);

        return ToDto(entity, branchName, values, definitions);
    }

    public async Task<IReadOnlyList<CardholderSummaryDto>> SearchAsync(CardholderSearchRequest request, CancellationToken ct = default)
    {
        var all = await _uow.Repository<CardholderEntity>()
            .FindAsync(c => c.OrganizationId == request.OrganizationId, ct);

        IEnumerable<CardholderEntity> query = all;

        if (request.BranchId is { } branchId)
        {
            query = query.Where(c => c.BranchId == branchId);
        }

        if (!string.IsNullOrWhiteSpace(request.Department))
        {
            query = query.Where(c => string.Equals(c.Department, request.Department, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            var text = request.SearchText.Trim();
            query = query.Where(c =>
                (c.FullName?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Email?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Phone?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.IdNumber?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Department?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Designation?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var branches = await _uow.Repository<BranchEntity>().FindAsync(b => b.OrganizationId == request.OrganizationId, ct);
        var branchNames = branches.ToDictionary(b => b.Id, b => b.Name);

        return query
            .OrderBy(c => c.FullName)
            .Select(c => new CardholderSummaryDto
            {
                Id = c.Id,
                BranchId = c.BranchId,
                BranchName = c.BranchId is { } bid && branchNames.TryGetValue(bid, out var name) ? name : null,
                FullName = c.FullName,
                Email = c.Email,
                Phone = c.Phone,
                Department = c.Department,
                Designation = c.Designation,
                IdNumber = c.IdNumber,
                ModifiedDate = c.ModifiedDate,
            })
            .ToList();
    }

    public async Task<Result<long>> CreateAsync(CreateCardholderRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Result<long>.Failure("Full name is required.");
        }

        var validation = await ValidateCustomFieldsAsync(request.OrganizationId, request.CustomFieldValues, ct);
        if (validation is not null)
        {
            return Result<long>.Failure(validation);
        }

        var entity = new CardholderEntity
        {
            OrganizationId = request.OrganizationId,
            BranchId = request.BranchId,
            FullName = request.FullName.Trim(),
            ExternalRefId = request.ExternalRefId,
            Email = request.Email,
            Phone = request.Phone,
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            Department = request.Department,
            Designation = request.Designation,
            IdNumber = request.IdNumber,
        };

        await _uow.Repository<CardholderEntity>().AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct); // need entity.Id before writing child custom-field-value rows

        await UpsertCustomFieldValuesAsync(entity.Id, request.CustomFieldValues, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<long>.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(UpdateCardholderRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Result.Failure("Full name is required.");
        }

        var entity = await _uow.Repository<CardholderEntity>().GetByIdAsync(request.Id, ct);
        if (entity is null)
        {
            return Result.Failure($"Cardholder {request.Id} was not found.");
        }

        var validation = await ValidateCustomFieldsAsync(entity.OrganizationId, request.CustomFieldValues, ct);
        if (validation is not null)
        {
            return Result.Failure(validation);
        }

        entity.BranchId = request.BranchId;
        entity.FullName = request.FullName.Trim();
        entity.ExternalRefId = request.ExternalRefId;
        entity.Email = request.Email;
        entity.Phone = request.Phone;
        entity.DateOfBirth = request.DateOfBirth;
        entity.Gender = request.Gender;
        entity.Department = request.Department;
        entity.Designation = request.Designation;
        entity.IdNumber = request.IdNumber;

        _uow.Repository<CardholderEntity>().Update(entity);
        await UpsertCustomFieldValuesAsync(entity.Id, request.CustomFieldValues, ct);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> SoftDeleteAsync(long id, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<CardholderEntity>().GetByIdAsync(id, ct);
        if (entity is null)
        {
            return Result.Failure($"Cardholder {id} was not found.");
        }

        _uow.Repository<CardholderEntity>().SoftDelete(entity);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<IReadOnlyList<CustomFieldDefinitionDto>> GetCustomFieldDefinitionsAsync(long organizationId, CancellationToken ct = default)
    {
        var definitions = await _uow.Repository<CustomFieldDefinitionEntity>()
            .FindAsync(d => d.OrganizationId == organizationId, ct);

        return definitions
            .OrderBy(d => d.Label)
            .Select(ToDefinitionDto)
            .ToList();
    }

    public async Task<Result<long>> CreateCustomFieldDefinitionAsync(CreateCustomFieldDefinitionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FieldKey) || string.IsNullOrWhiteSpace(request.Label))
        {
            return Result<long>.Failure("Field key and label are required.");
        }

        var duplicate = await _uow.Repository<CustomFieldDefinitionEntity>()
            .AnyAsync(d => d.OrganizationId == request.OrganizationId &&
                           d.FieldKey.ToLower() == request.FieldKey.Trim().ToLower(), ct);
        if (duplicate)
        {
            return Result<long>.Failure($"A custom field with key '{request.FieldKey}' already exists.");
        }

        var entity = new CustomFieldDefinitionEntity
        {
            OrganizationId = request.OrganizationId,
            FieldKey = request.FieldKey.Trim(),
            Label = request.Label.Trim(),
            DataType = request.DataType,
            IsRequired = request.IsRequired,
            AppliesToCategory = request.AppliesToCategory,
        };

        await _uow.Repository<CustomFieldDefinitionEntity>().AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<long>.Success(entity.Id);
    }

    public async Task<Result> DeleteCustomFieldDefinitionAsync(long id, CancellationToken ct = default)
    {
        var entity = await _uow.Repository<CustomFieldDefinitionEntity>().GetByIdAsync(id, ct);
        if (entity is null)
        {
            return Result.Failure($"Custom field {id} was not found.");
        }

        _uow.Repository<CustomFieldDefinitionEntity>().SoftDelete(entity);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }

    // ===================== Helpers =====================

    private async Task<string?> ValidateCustomFieldsAsync(long organizationId, List<CardholderCustomFieldValueDto> values, CancellationToken ct)
    {
        if (values.Count == 0) return null;

        var definitions = await _uow.Repository<CustomFieldDefinitionEntity>()
            .FindAsync(d => d.OrganizationId == organizationId, ct);
        var byId = definitions.ToDictionary(d => d.Id);

        foreach (var v in values)
        {
            if (!byId.TryGetValue(v.CustomFieldDefinitionId, out var def))
            {
                return $"Custom field {v.CustomFieldDefinitionId} does not belong to this organization.";
            }

            if (def.IsRequired && string.IsNullOrWhiteSpace(v.Value))
            {
                return $"'{def.Label}' is required.";
            }
        }

        return null;
    }

    private async Task UpsertCustomFieldValuesAsync(long cardholderId, List<CardholderCustomFieldValueDto> values, CancellationToken ct)
    {
        if (values.Count == 0) return;

        var existing = await _uow.Repository<CardholderCustomFieldValueEntity>()
            .FindAsync(v => v.CardholderId == cardholderId, ct);
        var existingByDefId = existing.ToDictionary(v => v.CustomFieldDefinitionId);

        foreach (var incoming in values)
        {
            if (existingByDefId.TryGetValue(incoming.CustomFieldDefinitionId, out var row))
            {
                row.Value = incoming.Value;
                _uow.Repository<CardholderCustomFieldValueEntity>().Update(row);
            }
            else
            {
                await _uow.Repository<CardholderCustomFieldValueEntity>().AddAsync(new CardholderCustomFieldValueEntity
                {
                    CardholderId = cardholderId,
                    CustomFieldDefinitionId = incoming.CustomFieldDefinitionId,
                    Value = incoming.Value,
                }, ct);
            }
        }
    }

    private async Task<string?> ResolveBranchNameAsync(long? branchId, CancellationToken ct)
    {
        if (branchId is not { } id) return null;
        var branch = await _uow.Repository<BranchEntity>().GetByIdAsync(id, ct);
        return branch?.Name;
    }

    private static CardholderDto ToDto(
        CardholderEntity c,
        string? branchName,
        IReadOnlyList<CardholderCustomFieldValueEntity> values,
        IReadOnlyList<CustomFieldDefinitionEntity> definitions)
    {
        var valuesByDefId = values.ToDictionary(v => v.CustomFieldDefinitionId, v => v.Value);

        return new CardholderDto
        {
            Id = c.Id,
            OrganizationId = c.OrganizationId,
            BranchId = c.BranchId,
            BranchName = branchName,
            FullName = c.FullName,
            ExternalRefId = c.ExternalRefId,
            Email = c.Email,
            Phone = c.Phone,
            DateOfBirth = c.DateOfBirth,
            Gender = c.Gender,
            Department = c.Department,
            Designation = c.Designation,
            IdNumber = c.IdNumber,
            IsAnonymized = c.IsAnonymized,
            CreatedDate = c.CreatedDate,
            ModifiedDate = c.ModifiedDate,
            CustomFieldValues = definitions
                .OrderBy(d => d.Label)
                .Select(d => new CardholderCustomFieldValueDto
                {
                    CustomFieldDefinitionId = d.Id,
                    FieldKey = d.FieldKey,
                    Label = d.Label,
                    DataType = d.DataType,
                    IsRequired = d.IsRequired,
                    Value = valuesByDefId.TryGetValue(d.Id, out var v) ? v : null,
                })
                .ToList(),
        };
    }

    private static CustomFieldDefinitionDto ToDefinitionDto(CustomFieldDefinitionEntity d) => new()
    {
        Id = d.Id,
        OrganizationId = d.OrganizationId,
        FieldKey = d.FieldKey,
        Label = d.Label,
        DataType = d.DataType,
        IsRequired = d.IsRequired,
        AppliesToCategory = d.AppliesToCategory,
    };
}
