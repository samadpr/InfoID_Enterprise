using System.Text.Json;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Audit;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Application.Mappings;
using InfoID.LicenseManager.Domain.Entities;
using InfoID.LicenseManager.Domain.Rules;

namespace InfoID.LicenseManager.Application.Services;

public sealed class AuditService : IAuditService
{
    private readonly IAuditLogRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;

    public AuditService(IAuditLogRepository repository, ICurrentUser currentUser, TimeProvider time)
    {
        _repository = repository;
        _currentUser = currentUser;
        _time = time;
    }

    public Task LogAsync(string action, object? details = null, long? adminUserIdOverride = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var adminUserId = adminUserIdOverride ?? _currentUser.AdminUserId;
        var address = _currentUser.ClientAddress;
        var detailsJson = details is null ? null : JsonSerializer.Serialize(details);
        var actionDate = AuditChain.TruncateToMicroseconds(_time.GetUtcNow().UtcDateTime);

        return _repository.AppendAsync(previousHash =>
        {
            var entry = new AdminAuditLog
            {
                AdminUserId = adminUserId,
                Action = action,
                IPAddress = address,
                ActionDate = actionDate,
                DetailsJson = detailsJson,
                PreviousHash = previousHash,
            };
            entry.RecordHash = AuditChain.ComputeHash(entry);
            return entry;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLogEntryDto>> SearchAsync(AuditSearchDto search, CancellationToken cancellationToken = default)
    {
        static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, 100)];

        var from = search.From?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toExclusive = search.To?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await _repository.SearchAsync(
            new AuditSearchCriteria(from, toExclusive, Clean(search.Who), Clean(search.Action), Math.Clamp(search.Take, 1, 1000)), cancellationToken);
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<AuditLogEntryDto>> GetRecentAsync(int take = 200, CancellationToken cancellationToken = default)
    {
        var rows = await _repository.GetRecentAsync(Math.Clamp(take, 1, 1000), cancellationToken);
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<long?> VerifyChainAsync(CancellationToken cancellationToken = default)
    {
        string? previous = null;
        await foreach (var row in _repository.StreamAllAsync(cancellationToken))
        {
            if (!AuditChain.IsIntact(row, previous)) return row.Id;
            previous = row.RecordHash;
        }

        return null;
    }
}
