namespace InfoID.Desktop.Core.Services;

/// <summary>
/// Small, flat bag of user-level app preferences that must survive an app restart
/// (Part 7: "Autosave... persist the preference... survive application restart").
/// Deliberately a plain POCO -- it's a JSON-serialization DTO, not a bindable model;
/// consumers (e.g. CardDesignTabViewModel) copy the value they need into their own
/// [ObservableProperty] rather than binding to this directly.
/// </summary>
public sealed class UserPreferences
{
    /// <summary>Default matches the existing intended behaviour before this setting
    /// existed: the Card Designer always autosaved. Off is an explicit opt-out.</summary>
    public bool AutosaveEnabled { get; set; } = true;
}
