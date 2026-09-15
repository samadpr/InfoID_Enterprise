namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>Matches the three levels called for in the Design Checker requirement
/// (Part 79): Error/Warning/Info, ordered worst-to-least-severe so a default sort by
/// this enum shows the most important issues first.</summary>
public enum DesignIssueSeverity
{
    Error,
    Warning,
    Info,
}
