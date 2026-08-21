using InfoID.Domain.Common.Enums;

namespace InfoID.Application.Dtos.CardholderModule;

/// <summary>
/// One cardholder record (FR-DAT-5 "in-app record grid" backing DTO, and the
/// Cardholder Management module's Add/Edit form model). Mirrors Domain Model
/// Module E's Cardholder table plus its custom-field values flattened onto
/// the same DTO for a simpler Desktop binding surface (list of key/value
/// pairs instead of the operator having to know CustomFieldDefinitionId).
/// </summary>
public class CardholderDto
{
    public long Id { get; set; }
    public long OrganizationId { get; set; }
    public long? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ExternalRefId { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? IdNumber { get; set; }
    public bool IsAnonymized { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public List<CardholderCustomFieldValueDto> CustomFieldValues { get; set; } = [];
}

/// <summary>Lightweight row for the record grid (FR-DAT-5) -- skips custom
/// field values, which the grid doesn't render as columns today.</summary>
public class CardholderSummaryDto
{
    public long Id { get; set; }
    public long? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? IdNumber { get; set; }
    public DateTime? ModifiedDate { get; set; }
}

public class CardholderCustomFieldValueDto
{
    public long CustomFieldDefinitionId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public CustomFieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public string? Value { get; set; }
}

public class CustomFieldDefinitionDto
{
    public long Id { get; set; }
    public long OrganizationId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public CustomFieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public string? AppliesToCategory { get; set; }
}

public class CreateCardholderRequest
{
    public long OrganizationId { get; set; }
    public long? BranchId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ExternalRefId { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? IdNumber { get; set; }
    public List<CardholderCustomFieldValueDto> CustomFieldValues { get; set; } = [];
}

public class UpdateCardholderRequest
{
    public long Id { get; set; }
    public long? BranchId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ExternalRefId { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? IdNumber { get; set; }
    public List<CardholderCustomFieldValueDto> CustomFieldValues { get; set; } = [];
}

public class CreateCustomFieldDefinitionRequest
{
    public long OrganizationId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public CustomFieldDataType DataType { get; set; } = CustomFieldDataType.Text;
    public bool IsRequired { get; set; }
    public string? AppliesToCategory { get; set; }
}

/// <summary>Search/filter parameters for the record grid (FR-DAT-5: "search,
/// filter, sort"). Kept as a simple predicate builder rather than a full
/// PagedRequest/PagedResult<T> pipeline -- Cardholder volumes for a single
/// on-premise installation are small enough that client-side paging in the
/// grid is adequate for now; can be upgraded later without changing the
/// Desktop-facing DTO shape.</summary>
public class CardholderSearchRequest
{
    public long OrganizationId { get; set; }
    public long? BranchId { get; set; }
    public string? SearchText { get; set; }
    public string? Department { get; set; }
}
