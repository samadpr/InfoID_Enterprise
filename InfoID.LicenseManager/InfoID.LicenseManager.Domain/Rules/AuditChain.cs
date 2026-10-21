using System.Security.Cryptography;
using System.Text;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Domain.Rules;

/// <summary>
/// The tamper-evidence rule of the admin audit log (FR-ADM-3): every row's hash covers its
/// own content plus the previous row's hash, so editing or deleting a past row breaks the
/// chain. The hash format is part of the stored data and must stay byte-identical to the
/// one InfoID.Licensing.Server wrote, or existing rows would be reported as tampered.
/// </summary>
public static class AuditChain
{
    public static string ComputeHash(AdminAuditLog entry)
    {
        var content = string.Join('|',
            entry.AdminUserId?.ToString() ?? "",
            entry.Action,
            entry.IPAddress ?? "",
            entry.ActionDate.ToUniversalTime().ToString("O"),
            entry.DetailsJson ?? "",
            entry.PreviousHash ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    /// <summary>PostgreSQL stores microseconds; truncating before hashing makes the hash
    /// recomputed from the stored row equal the one computed at write time.</summary>
    public static DateTime TruncateToMicroseconds(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);

    /// <summary>True when the row links to <paramref name="expectedPreviousHash"/> (the
    /// previous row's hash, null for the first row) and its own hash matches its content.</summary>
    public static bool IsIntact(AdminAuditLog row, string? expectedPreviousHash) =>
        row.PreviousHash == expectedPreviousHash && row.RecordHash == ComputeHash(row);
}
