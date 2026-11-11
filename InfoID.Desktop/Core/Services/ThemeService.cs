using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using System;
using System.Threading.Tasks;


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

    private readonly IUserPreferencesService _preferences;

    public ThemeService(IUserPreferencesService preferences) => _preferences = preferences;

    //public void InitializeFromSystem()
    //{
    //    var detected = DetectSystemTheme();
    //    ApplyTheme(detected);
    //}

    public void InitializeFromSystem()
    {
        // The user's last choice wins; Windows' theme is only used before they ever chose.
        var saved = _preferences.Current.Theme;
        var theme = Enum.TryParse<AppTheme>(saved, ignoreCase: true, out var chosen)
            ? chosen
            : DetectSystemTheme();
        ApplyTheme(theme);
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

    //public void Toggle()
    //{
    //    ApplyTheme(CurrentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
    //}

    public void Toggle()
    {
        var next = CurrentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        ApplyTheme(next);
        _ = SaveAsync(next);
    }

    private async Task SaveAsync(AppTheme theme)
    {
        try
        {
            await _preferences.SetThemeAsync(theme.ToString());
        }
        catch (Exception)
        {
            // Not critical: the theme still changes now, it just won't be remembered.
        }
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
