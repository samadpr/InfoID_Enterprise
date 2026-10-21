namespace InfoID.LicenseManager.Web.Ui;

public sealed record Crumb(string Label, string? Url);

public sealed record SidebarModel(string? ActiveController, int PendingRequests, bool IsSuperAdmin);

public sealed record TopbarModel(IReadOnlyList<Crumb> Crumbs, string DisplayName, string Role, int PendingRequests, string SearchText);

/// <summary>Content of the shared empty state: an icon, a short title, one helpful sentence and an optional action link.</summary>
public sealed record EmptyState(string Icon, string Title, string Text, string? ActionLabel = null, string? ActionUrl = null);
