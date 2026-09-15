using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace InfoID.Desktop.Core.Dialogs;

/// <summary>Thin wrapper over Avalonia's native file picker (Part 10/12/13: Insert
/// Image / Photo / Signature workflows; Part 9/62: .infoid export/import). Kept separate
/// from IDialogService, which is for InfoID's own custom dialog ViewModels, not OS file
/// pickers.</summary>
public interface IFilePickerService
{
    /// <summary>Shows the native "open file" dialog filtered to common image formats
    /// and returns the picked file's local path, or null if the user cancelled.</summary>
    Task<string?> PickImageFileAsync(string title);

    /// <summary>Generic "open file" dialog for a specific extension (e.g. .infoid
    /// import). Returns the picked file's local path, or null if cancelled.</summary>
    Task<string?> PickOpenFileAsync(string title, string filterName, IReadOnlyList<string> patterns);

    /// <summary>Generic "save file" dialog for a specific extension (e.g. .infoid
    /// export). Returns the chosen local path (with the extension applied), or null if
    /// cancelled.</summary>
    Task<string?> PickSaveFileAsync(string title, string filterName, IReadOnlyList<string> patterns, string suggestedFileName, string defaultExtension);
}

public sealed class FilePickerService : IFilePickerService
{
    public async Task<string?> PickImageFileAsync(string title)
    {
        var topLevel = GetOwnerWindow();
        if (topLevel is null) return null;

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" } },
            },
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickOpenFileAsync(string title, string filterName, IReadOnlyList<string> patterns)
    {
        var topLevel = GetOwnerWindow();
        if (topLevel is null) return null;

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new(filterName) { Patterns = patterns },
            },
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string filterName, IReadOnlyList<string> patterns, string suggestedFileName, string defaultExtension)
    {
        var topLevel = GetOwnerWindow();
        if (topLevel is null) return null;

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = defaultExtension,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new(filterName) { Patterns = patterns },
            },
        });

        return result?.TryGetLocalPath();
    }

    private static Window? GetOwnerWindow() =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
