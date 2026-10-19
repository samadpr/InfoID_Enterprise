using System.Text.Encodings.Web;
using System.Text.Json;
using InfoID.Domain.Common.Interfaces;
using InfoID.Domain.Entities.ApprovalAuditModule;
using InfoID.Shared.Logging;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Infrastructure.Auditing;

public sealed class AuditTrailOptions
{
    /// <summary>The anchor file: the id and hash of the newest entry, kept outside the
    /// database so that removing the most recent entries is detected too.</summary>
    public string AnchorPath { get; init; } = string.Empty;
}

/// <summary>Windows user name (the Desktop app adds the signed-in InfoID account).</summary>
public sealed class DefaultAuditActorProvider : IAuditActorProvider
{
    public string CurrentActor => Environment.UserName;
}

/// <summary>
/// Appends entries to the AuditLog table as a hash chain (see <see cref="AuditHasher"/>).
/// Appends are serialized across the whole app and use their own short-lived DbContext,
/// so they never pick up (or save) a caller's half-finished changes.
/// </summary>
public sealed class AuditTrail : IAuditTrail
{
    private static readonly AppLogger Log = AppLog.For<AuditTrail>();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep names readable (é, ü, Arabic...) in the viewer
        WriteIndented = false,
    };

    private const int MaxValueLength = 8000;

    private readonly DbContextOptions<InfoIdDbContext> _dbOptions;
    private readonly IAuditActorProvider _actor;
    private readonly AuditTrailOptions _options;

    public AuditTrail(DbContextOptions<InfoIdDbContext> dbOptions, IAuditActorProvider actor, AuditTrailOptions options)
    {
        _dbOptions = dbOptions;
        _actor = actor;
        _options = options;
    }

    public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            await Gate.WaitAsync(ct);
            try
            {
                await using var db = new InfoIdDbContext(_dbOptions);
                var previous = await db.AuditLogs.AsNoTracking()
                    .OrderByDescending(a => a.Id)
                    .Select(a => a.RecordHash)
                    .FirstOrDefaultAsync(ct);

                var now = DateTime.UtcNow;
                var row = new AuditLog
                {
                    Module = Truncate(entry.Module, 50)!,
                    Action = Truncate(entry.Action, 100)!,
                    EntityName = Truncate(entry.EntityName, 100),
                    EntityId = entry.EntityId,
                    BeforeValueJson = Serialize(entry.Before),
                    AfterValueJson = Serialize(entry.After),
                    ActionDate = now,
                    CreatedDate = now,
                    CreatedBy = SafeActor(),
                    Description = Truncate(entry.Description, 500),
                    MachineName = Truncate(Environment.MachineName, 100),
                    PreviousHash = previous,
                };
                row.RecordHash = AuditHasher.Compute(row);

                db.AuditLogs.Add(row);
                await db.SaveChangesAsync(ct);
                AuditAnchor.Write(_options.AnchorPath, row.Id, row.RecordHash);
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (Exception ex)
        {
            // Never let auditing break the action being audited; the technical log has it.
            Log.Error($"Couldn't write an audit entry ({entry.Module}/{entry.Action})", ex);
        }
    }

    private string? SafeActor()
    {
        try { return Truncate(_actor.CurrentActor, 200); }
        catch { return Environment.UserName; }
    }

    internal static string? Serialize(object? value)
    {
        if (value is null) return null;
        var json = value as string ?? JsonSerializer.Serialize(value, value.GetType(), Json);
        return json.Length > MaxValueLength ? json[..MaxValueLength] : json;
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length > max ? value[..max] : value;
}

/// <summary>The anchor file (AuditAnchor.json): newest entry id + hash, with a MAC.</summary>
internal static class AuditAnchor
{
    private static readonly AppLogger Log = AppLog.For("InfoID.Audit.Anchor");

    internal sealed class Data
    {
        public long LastId { get; set; }
        public string LastHash { get; set; } = string.Empty;
        public string Mac { get; set; } = string.Empty;
    }

    public static void Write(string path, long lastId, string lastHash)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var data = new Data { LastId = lastId, LastHash = lastHash, Mac = AuditHasher.AnchorMac(lastId, lastHash) };
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Couldn't update the audit anchor", ex);
        }
    }

    /// <summary>(null, false) = no anchor; (null, true) = the anchor file was modified.</summary>
    public static (Data? Anchor, bool Tampered) Read(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return (null, false);
        try
        {
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path));
            if (data is null) return (null, true);
            return data.Mac == AuditHasher.AnchorMac(data.LastId, data.LastHash) ? (data, false) : (null, true);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return (null, true);
        }
    }
}
