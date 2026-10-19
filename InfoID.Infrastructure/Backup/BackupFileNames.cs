using System.Globalization;
using System.Text.RegularExpressions;

namespace InfoID.Infrastructure.Backup;

/// <summary>
/// Backup file naming: InfoID-Backup-2026-10-12_113005[-auto|-safety].infoidbak.
/// The date and kind are in the name so the backup list (and automatic pruning) never has
/// to open -- or decrypt -- a file to know what it is. Local time, because that's what
/// the user sees in Explorer.
/// </summary>
public static partial class BackupFileNames
{
    public const string Extension = ".infoidbak";
    public const string Pattern = "*" + Extension;
    private const string Prefix = "InfoID-Backup-";
    private const string StampFormat = "yyyy-MM-dd_HHmmss";

    public static string Create(DateTime local, BackupKind kind) =>
        Prefix + local.ToString(StampFormat, CultureInfo.InvariantCulture) + kind switch
        {
            BackupKind.Automatic => "-auto",
            BackupKind.SafetyBeforeRestore => "-safety",
            _ => string.Empty,
        } + Extension;

    /// <summary>A name that doesn't exist yet in <paramref name="folder"/> (adds -2, -3 ...
    /// if two backups are made within the same second).</summary>
    public static string CreateUniquePath(string folder, DateTime local, BackupKind kind)
    {
        var path = Path.Combine(folder, Create(local, kind));
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, Path.GetFileNameWithoutExtension(Create(local, kind)) + $"-{n}" + Extension);
        return path;
    }

    /// <summary>Reads date and kind back from a name made by <see cref="Create"/>.
    /// Renamed or foreign files are reported as Manual, dated by their file time.</summary>
    public static (DateTime? Local, BackupKind Kind) Parse(string fileName)
    {
        var match = NameRegex().Match(Path.GetFileName(fileName));
        if (!match.Success) return (null, BackupKind.Manual);

        DateTime? stamp = DateTime.TryParseExact(match.Groups["stamp"].Value, StampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var parsed) ? parsed : null;
        var kind = match.Groups["kind"].Value.ToLowerInvariant() switch
        {
            "auto" => BackupKind.Automatic,
            "safety" => BackupKind.SafetyBeforeRestore,
            _ => BackupKind.Manual,
        };
        return (stamp, kind);
    }

    [GeneratedRegex(@"^InfoID-Backup-(?<stamp>\d{4}-\d{2}-\d{2}_\d{6})(?:-(?<kind>auto|safety))?(?:-\d+)?\.infoidbak$", RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();
}
