using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>
/// One row of the Layers panel's tree -- either a single element (a "leaf") or a group
/// header standing in for every element that shares one GroupId (see DesignerElement.
/// GroupId, and CardDesignTabViewModel.Group()/Ungroup()). Rebuilt by
/// CardDesignTabViewModel.RefreshLayerTree() whenever anything that affects display
/// order/grouping/name/visibility/lock changes -- not a live view over the document, a
/// fresh snapshot each time, which is why DisplayName/Visible/Locked are plain mirrored
/// values (computed once at build time, including the aggregate case for a group) rather
/// than bindings straight through to one element's own properties.
/// </summary>
public sealed partial class LayerNode : ObservableObject
{
    /// <summary>Set for a leaf row, null for a group header.</summary>
    public DesignerElement? Element { get; }

    /// <summary>Set for a group header row, null for a leaf.</summary>
    public string? GroupId { get; }

    public bool IsGroup => GroupId is not null;

    /// <summary>Populated only for a group header -- the elements sharing GroupId,
    /// already ordered front-most (highest ZIndex) first, same order the top-level
    /// tree itself uses.</summary>
    public ObservableCollection<LayerNode> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Mirrors whether this row's element(s) are part of the current
    /// selection -- refreshed by CardDesignTabViewModel.RefreshLayerSelectionHighlight,
    /// hooked into the same NotifySelectionChanged() every other selection-driven UI
    /// update already goes through.</summary>
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>For a group: true if ANY member is visible (matches "eye" toggling all
    /// members to hidden only once none are already hidden-by-default reading of an
    /// all-hidden group as hidden). For a leaf: mirrors Element.Visible directly.</summary>
    [ObservableProperty]
    private bool _visible = true;

    /// <summary>For a group: true only if EVERY member is locked (a group with a mix of
    /// locked/unlocked members reads as unlocked, since it can still be worked with).
    /// For a leaf: mirrors Element.Locked directly.</summary>
    [ObservableProperty]
    private bool _locked;

    /// <summary>True while this row is the one being dragged -- toggled by
    /// CardDesignerView.axaml.cs around the DoDragDropAsync call, purely a visual cue
    /// (reduced opacity), never persisted or read back.</summary>
    [ObservableProperty]
    private bool _isDragging;

    /// <summary>True while a group folder's name is being edited inline -- same
    /// double-click-to-edit shape as the tab strip's own rename (CardDesignTabViewModel.
    /// IsRenaming/RenameText for tab titles), scoped per-row instead of per-tab since
    /// many rows can exist at once.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = string.Empty;

    public LayerNode(DesignerElement element)
    {
        Element = element;
        DisplayName = element.Name;
        Visible = element.Visible;
        Locked = element.Locked;
    }

    public LayerNode(string groupId, string displayName, IReadOnlyList<DesignerElement> members)
    {
        GroupId = groupId;
        DisplayName = displayName;
        Visible = members.Count == 0 || members.Any(m => m.Visible);
        Locked = members.Count > 0 && members.All(m => m.Locked);
    }
}
