using System;

namespace InfoID.Desktop.Core.Services;

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// Centralized theme abstraction. Detects the OS theme once at startup and lets the
/// header's theme toggle flip between Light and Dark afterwards. InfoID intentionally
/// never exposes a user-facing "System" option -- once the user picks a theme it stays
/// picked for the session.
/// </summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    event EventHandler<AppTheme>? ThemeChanged;

    /// <summary>Detects the current OS theme and applies it. Call once on startup.</summary>
    void InitializeFromSystem();

    void ApplyTheme(AppTheme theme);

    void Toggle();
}
