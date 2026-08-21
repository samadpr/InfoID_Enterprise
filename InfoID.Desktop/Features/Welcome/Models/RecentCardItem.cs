using System;

namespace InfoID.Desktop.Features.Welcome.Models;

/// <summary>
/// A recently opened/created card design shown on the Welcome page. Today this is
/// populated with sample data via <see cref="Services.IRecentCardService"/>; later the
/// same shape will be filled from the Template/Card repositories.
/// </summary>
public sealed class RecentCardItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Subtitle { get; init; }
    public DateTime LastModified { get; init; }
}
