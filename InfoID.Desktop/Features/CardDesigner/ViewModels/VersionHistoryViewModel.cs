using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.CardDesigner.Models;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>
/// Version History (Part 60): lists saved snapshots for the current design and lets the
/// user restore one. Returns the selected version's id (or null if the dialog was just
/// closed) -- the actual snapshot fetch and tab-opening happens in
/// CardDesignerViewModel.ShowVersionHistory, since only it can open a new tab.
///
/// Restoring always opens the old snapshot as a brand-new, unsaved tab rather than
/// overwriting the currently open one or deleting anything: "Never destroy the currently
/// saved version simply because another version is restored" (Part 60). The user can
/// compare the restored copy against what they currently have open and decide whether to
/// keep it before saving over anything.
/// </summary>
public sealed partial class VersionHistoryViewModel : DialogViewModelBase<long?>
{
    public ObservableCollection<TemplateVersionSummary> Versions { get; }

    public bool HasVersions => Versions.Count > 0;

    public VersionHistoryViewModel(IReadOnlyList<TemplateVersionSummary> versions)
    {
        Versions = new ObservableCollection<TemplateVersionSummary>(versions);
    }

    [RelayCommand]
    private void Restore(TemplateVersionSummary version) => RequestClose(version.VersionId);

    [RelayCommand]
    private void Close() => RequestClose(null);
}
