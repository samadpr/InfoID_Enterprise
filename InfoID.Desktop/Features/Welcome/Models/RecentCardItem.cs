using System;

namespace InfoID.Desktop.Features.Welcome.Models;

public sealed class RecentCardItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Subtitle { get; init; }
    public DateTime LastModified { get; init; }
    public bool IsPinned { get; init; }
}