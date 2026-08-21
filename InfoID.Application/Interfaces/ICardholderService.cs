using InfoID.Application.Dtos.CardholderModule;
using InfoID.Shared.Common;

namespace InfoID.Application.Interfaces;

/// <summary>
/// Application-layer service for Module E — Cardholder Management (SRS
/// FR-DAT-5 record grid, FR-DES-11's CustomFieldDefinition source, Domain
/// Model Module E). Desktop talks to this interface only -- never to EF/
/// IUnitOfWork directly -- same layering as ITemplateService/IBranchService.
/// </summary>
public interface ICardholderService
{
    Task<CardholderDto?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<CardholderSummaryDto>> SearchAsync(CardholderSearchRequest request, CancellationToken ct = default);

    Task<Result<long>> CreateAsync(CreateCardholderRequest request, CancellationToken ct = default);

    Task<Result> UpdateAsync(UpdateCardholderRequest request, CancellationToken ct = default);

    /// <summary>Soft-delete (Cancelled = true) -- InfoID never hard-deletes
    /// business rows, per the project's locked-in soft-delete convention.</summary>
    Task<Result> SoftDeleteAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<CustomFieldDefinitionDto>> GetCustomFieldDefinitionsAsync(long organizationId, CancellationToken ct = default);

    Task<Result<long>> CreateCustomFieldDefinitionAsync(CreateCustomFieldDefinitionRequest request, CancellationToken ct = default);

    Task<Result> DeleteCustomFieldDefinitionAsync(long id, CancellationToken ct = default);
}
