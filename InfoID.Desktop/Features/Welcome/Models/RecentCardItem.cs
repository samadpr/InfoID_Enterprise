using System;
using Avalonia.Media;

namespace InfoID.Desktop.Features.Welcome.Models;

public sealed class RecentCardItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Subtitle { get; init; }
    public DateTime LastModified { get; init; }
    public bool IsPinned { get; init; }

    /// <summary>Null until a thumbnail has actually been generated for this design (see
    /// IThumbnailService) and successfully loaded -- the View falls back to a plain tile
    /// background when this is null, rather than showing a broken image or a fake
    /// placeholder picture. Deliberately a pre-loaded IImage rather than a file path +
    /// XAML value converter: RecentCardService loads the bitmap once per list build, so
    /// the View only ever needs a plain, converter-free {Binding ThumbnailImage}.</summary>
    public IImage? ThumbnailImage { get; init; }
}