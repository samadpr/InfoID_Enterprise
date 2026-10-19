using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using InfoID.Domain.Entities.ApprovalAuditModule;

namespace InfoID.Infrastructure.Auditing;

/// <summary>
/// The audit log's hash chain (FR-REP-7). Each entry's RecordHash is an HMAC-SHA256 over
/// all of its content plus the previous entry's hash, so changing, removing, inserting or
/// reordering any entry breaks the chain from that point on.
///
/// Why keyed (HMAC) rather than a plain SHA-256: with a plain hash, anyone who knows the
/// algorithm could edit an entry and simply recompute every hash after it. The key is
/// built into InfoID, which raises that bar to reverse-engineering the application. On a
/// single PC whose owner has full control this is tamper-EVIDENT, not tamper-proof.
/// </summary>
internal static class AuditHasher
{
    public const string Version = "v1";

    // Fixed per product version; changing it would invalidate every existing chain, so a
    // future key must come with a new Version prefix (old entries keep verifying as v1).
    private static readonly byte[] KeyV1 = Convert.FromHexString(
        "7A1C4E0B93D25F86C3A9174E2B8D60F5E14C79A2B03D5E8F6127C94A0B3E5D71");

    public static string Compute(AuditLog entry)
    {
        var canonical = Canonical(entry);
        var mac = HMACSHA256.HashData(KeyV1, Encoding.UTF8.GetBytes(canonical));
        return Version + ":" + Convert.ToHexStringLower(mac);
    }

    public static bool Matches(AuditLog entry) =>
        entry.RecordHash.StartsWith(Version + ":", StringComparison.Ordinal)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Compute(entry)), Encoding.ASCII.GetBytes(entry.RecordHash));

    /// <summary>MAC for the anchor file (last id + hash), so it can't be edited by hand.</summary>
    public static string AnchorMac(long lastId, string lastHash) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(KeyV1, Encoding.UTF8.GetBytes($"anchor|{lastId}|{lastHash}")));

    /// <summary>Length-prefixed fields, so no value can be shifted into its neighbour.</summary>
    private static string Canonical(AuditLog e)
    {
        var sb = new StringBuilder(256);
        void Add(string? value)
        {
            if (value is null) { sb.Append("-1:;"); return; }
            sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
        }

        Add(Version);
        Add(e.Module);
        Add(e.Action);
        Add(e.EntityName);
        Add(e.EntityId?.ToString(CultureInfo.InvariantCulture));
        Add(e.BeforeValueJson);
        Add(e.AfterValueJson);
        Add(e.ActionDate.Ticks.ToString(CultureInfo.InvariantCulture));
        Add(e.CreatedBy);
        Add(e.Description);
        Add(e.MachineName);
        Add(e.AppUserId?.ToString(CultureInfo.InvariantCulture));
        Add(e.Cancelled ? "1" : "0");
        Add(e.PreviousHash);
        return sb.ToString();
    }
}
