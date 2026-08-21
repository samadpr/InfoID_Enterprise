using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.ViewModels.Base;

/// <summary>
/// Base class for ViewModels that represent an open "document" workspace (e.g. a card
/// being designed). Not used yet -- the Card Designer is a single placeholder workspace
/// today -- but establishes the shape multi-document tabs (see reference screenshot
/// "New card 2 / New card 3") will need: a title, a dirty flag and a close command hook.
/// </summary>
public abstract partial class DocumentViewModelBase : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Untitled";

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// Unique id for the open document tab. Used by the future multi-document shell to
    /// tell tabs apart even when titles collide (e.g. two "New card" tabs).
    /// </summary>
    public Guid DocumentId { get; } = Guid.NewGuid();
}
