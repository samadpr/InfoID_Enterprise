using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;

namespace InfoID.Desktop.Shell.ViewModels;

/// <summary>
/// Help > About InfoID (Part 83). Deliberately minimal and honest: the version shown is
/// whatever the assembly actually reports, not a hand-typed marketing string that would
/// silently drift out of sync with what's really installed.
/// </summary>
public sealed partial class AboutViewModel : DialogViewModelBase<bool>
{
    public string AppName => "InfoID";

    public string VersionText
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "Development build" : $"Version {version.ToString(3)}";
        }
    }

    public string FrameworkText => "Avalonia UI, .NET";

    [RelayCommand]
    private void Close() => RequestClose(true);
}
