using System;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;

namespace InfoID.Desktop.Core.Services;

/// <summary>
/// Default <see cref="IThemeService"/> implementation. Reads the OS-reported theme via
/// Avalonia's cross-platform <see cref="IPlatformSettings"/> (works on Windows, macOS and
/// Linux -- no platform-specific APIs) and applies it by setting
/// <see cref="Application.RequestedThemeVariant"/>, which every DynamicResource-based
/// brush in Resources/ reacts to automatically.
/// </summary>
public sealed class ThemeService : IThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public event EventHandler<AppTheme>? ThemeChanged;

    public void InitializeFromSystem()
    {
        var detected = DetectSystemTheme();
        ApplyTheme(detected);
    }

    public void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;

        if (Avalonia.Application.Current is not null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = theme == AppTheme.Dark
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
        }

        ThemeChanged?.Invoke(this, theme);
    }

    public void Toggle()
    {
        ApplyTheme(CurrentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
    }

    private static AppTheme DetectSystemTheme()
    {
        try
        {
            var variant = Avalonia.Application.Current?.PlatformSettings?.GetColorValues()?.ThemeVariant;
            return variant == PlatformThemeVariant.Dark ? AppTheme.Dark : AppTheme.Light;
        }
        catch
        {
            // Some platforms/headless environments may not expose color values --
            // fall back to Light rather than let startup fail.
            return AppTheme.Light;
        }
    }
}
