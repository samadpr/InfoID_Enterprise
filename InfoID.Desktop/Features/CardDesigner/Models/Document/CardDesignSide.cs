using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

/// <summary>One physical side (front or back) of a card design -- fully independent
/// background and element set from the other side, per Part 4. ObservableCollection so
/// UI (canvas, layers panel) reacts to Add/Remove automatically; it still serializes
/// fine through System.Text.Json for the future .infoid export.</summary>
public sealed class CardDesignSide
{
    public CardSide Side { get; set; }
    public BackgroundSettings Background { get; set; } = new();
    public ObservableCollection<DesignerElement> Elements { get; set; } = new();
    public bool BackgroundLocked { get; set; }

    /// <summary>Display name for each layer-group folder, keyed by the same GroupId
    /// value elements carry on DesignerElement.GroupId. A folder can exist here with no
    /// elements pointing at it yet -- created empty via the Layers panel's "+" button,
    /// named, and only then populated by dragging elements in -- matching the reference
    /// app's "Front / Color-Black / UV" named-folder structure rather than InfoID's
    /// previous auto-generated "Group (3)" label with no folder-level identity.</summary>
    public Dictionary<string, string> GroupNames { get; set; } = new();
}