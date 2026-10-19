using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Domain.Common.Interfaces;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>What the audit log keeps about a design (Before/After of design entries).
/// Property names are part of the stored audit JSON -- don't rename.</summary>
internal sealed record DesignAuditSnapshot(string Name, string CardSize, int Version, int FrontElements, int BackElements);

/// <summary>
/// Groups ordinary design edits for the audit log. Autosave saves a design ~2 seconds
/// after every change, so auditing each save would flood the log. Rule, per design:
/// <list type="bullet">
/// <item>The first change after a quiet period (no design entry for <see cref="Interval"/>)
/// is recorded at once.</item>
/// <item>Later changes inside the interval are <b>held, never dropped</b>: one combined
/// "DesignChanged" entry (Before = state at the last entry, After = latest state) is
/// written when the interval ends, when the design's tab closes, or when InfoID exits.</item>
/// </list>
/// Created / renamed / version / deleted entries are always written at once (the
/// repository records those) and restart the interval.
/// </summary>
internal static class DesignEditAudit
{
    /// <summary>Grouping window (15 minutes; settable for tests).</summary>
    internal static TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For("InfoID.DesignEditAudit");
    private static readonly object Gate = new();
    private static readonly Dictionary<long, State> States = new();

    /// <summary>Writes held entries (timer, tab close, exit). Defaults to the Desktop
    /// audit helper, which uses its own DI scope; replaceable for tests.</summary>
    internal static Func<AuditEntry, Task> Writer { get; set; } = Core.Services.Audit.RecordAsync;

    /// <summary>Clock, replaceable for tests.</summary>
    internal static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    private sealed class State
    {
        public DateTime? LastEntryUtc;
        public DesignAuditSnapshot? PendingBefore;
        public DesignAuditSnapshot? PendingAfter;
        public Timer? Timer;
    }

    /// <summary>A real change was saved. Returns the Before/After to record now, or null
    /// when the change is held (a timer then writes it when the interval ends).</summary>
    public static (DesignAuditSnapshot Before, DesignAuditSnapshot After)? OnEdited(
        long templateId, DesignAuditSnapshot before, DesignAuditSnapshot after)
    {
        lock (Gate)
        {
            var state = GetState(templateId);
            var now = UtcNow();

            if (state.LastEntryUtc is not { } last || now - last >= Interval)
            {
                var from = state.PendingBefore ?? before;
                ClearPending(state);
                state.LastEntryUtc = now;
                return (from, after);
            }

            state.PendingBefore ??= before;
            state.PendingAfter = after;
            if (state.Timer is null)
            {
                var due = last + Interval - now;
                if (due < TimeSpan.Zero) due = TimeSpan.Zero;
                state.Timer = new Timer(_ => _ = FlushAsync(templateId), null, due, Timeout.InfiniteTimeSpan);
            }
            return null;
        }
    }

    /// <summary>An entry for this design was written at once (created, renamed). Returns
    /// the held "before" state, if any, so that entry can cover the held changes too.</summary>
    public static DesignAuditSnapshot? MarkWritten(long templateId)
    {
        lock (Gate)
        {
            var state = GetState(templateId);
            var pendingBefore = state.PendingBefore;
            ClearPending(state);
            state.LastEntryUtc = UtcNow();
            return pendingBefore;
        }
    }

    /// <summary>The design was deleted: nothing more to write for it.</summary>
    public static void Forget(long templateId)
    {
        lock (Gate)
        {
            if (!States.Remove(templateId, out var state)) return;
            ClearPending(state);
        }
    }

    /// <summary>Writes the held changes now -- one design (tab closed) or all (exit).</summary>
    public static async Task FlushAsync(long? templateId = null)
    {
        List<(long Id, DesignAuditSnapshot Before, DesignAuditSnapshot After)> due;
        lock (Gate)
        {
            due = States
                .Where(kv => (templateId is null || kv.Key == templateId) && kv.Value.PendingAfter is not null)
                .Select(kv => (kv.Key, kv.Value.PendingBefore!, kv.Value.PendingAfter!))
                .ToList();
            var now = UtcNow();
            foreach (var (id, _, _) in due)
            {
                var state = States[id];
                ClearPending(state);
                state.LastEntryUtc = now;
            }
        }

        foreach (var (id, before, after) in due)
        {
            try
            {
                await Writer(BuildChangedEntry(id, before, after)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn($"Couldn't record held design change for design {id}", ex);
            }
        }
    }

    /// <summary>"Design 'X' changed (front 5 → 7 elements)".</summary>
    public static AuditEntry BuildChangedEntry(long templateId, DesignAuditSnapshot before, DesignAuditSnapshot after)
    {
        var details = new List<string>();
        if (before.FrontElements != after.FrontElements)
            details.Add($"front {before.FrontElements} → {after.FrontElements} elements");
        if (before.BackElements != after.BackElements)
            details.Add($"back {before.BackElements} → {after.BackElements} elements");
        if (!string.Equals(before.CardSize, after.CardSize, StringComparison.OrdinalIgnoreCase))
            details.Add($"card size {before.CardSize} → {after.CardSize}");

        var description = $"Design '{after.Name}' changed" + (details.Count == 0 ? "" : $" ({string.Join(", ", details)})");
        return new AuditEntry(AuditModules.Designs, "DesignChanged", description)
        {
            EntityName = "Template",
            EntityId = templateId,
            Before = before,
            After = after,
        };
    }

    private static State GetState(long templateId)
    {
        if (!States.TryGetValue(templateId, out var state))
            States[templateId] = state = new State();
        return state;
    }

    private static void ClearPending(State state)
    {
        state.PendingBefore = null;
        state.PendingAfter = null;
        state.Timer?.Dispose();
        state.Timer = null;
    }

    /// <summary>Tests only.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            foreach (var state in States.Values) ClearPending(state);
            States.Clear();
        }
    }
}
