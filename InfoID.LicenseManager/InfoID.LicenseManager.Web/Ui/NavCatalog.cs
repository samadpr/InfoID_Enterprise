namespace InfoID.LicenseManager.Web.Ui;

/// <summary>One entry of the sidebar. <paramref name="SuperAdminOnly"/> only decides what is shown;
/// the controller's policy is what actually enforces access.</summary>
public sealed record NavItem(string Controller, string Action, string Label, string Icon, bool SuperAdminOnly = false);

public sealed record NavGroup(string Key, string Title, IReadOnlyList<NavItem> Items);

/// <summary>The single list the sidebar and the breadcrumbs are built from.</summary>
public static class NavCatalog
{
    public static IReadOnlyList<NavGroup> Groups { get; } =
    [
        new("overview", "Overview",
        [
            new("Dashboard", "Index", "Dashboard", "dashboard"),
        ]),
        new("licensing", "Licensing",
        [
            new("Licenses", "Index", "Activation Codes", "key"),
            new("Requests", "Index", "License Requests", "inbox"),
            new("Activations", "Generate", "Generate Serial Key", "lock"),
        ]),
        new("customers", "Customers & Plans",
        [
            new("Customers", "Index", "Customers", "users"),
            new("Plans", "Index", "Plans", "layers"),
        ]),
        new("monitoring", "Monitoring",
        [
            new("Audit", "Index", "Audit Log", "history"),
            new("Simulator", "Index", "Customer Simulator", "flask", SuperAdminOnly: true),
        ]),
    ];

    /// <summary>The sidebar entry for a controller (Activations covers the whole serial-key flow), or null for pages without one.</summary>
    public static NavItem? ForController(string? controller) =>
        Groups.SelectMany(g => g.Items).FirstOrDefault(i => string.Equals(i.Controller, controller, StringComparison.OrdinalIgnoreCase));
}
