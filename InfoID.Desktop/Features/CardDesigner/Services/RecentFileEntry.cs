using System;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>One tracked local .infoid file (Part: showing file-backed designs in Recent
/// Cards, since they have no database Template row to be tracked through).</summary>
public sealed class RecentFileEntry
{
    public string FilePath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime LastOpenedUtc { get; set; }
    public bool Pinned { get; set; }
}
