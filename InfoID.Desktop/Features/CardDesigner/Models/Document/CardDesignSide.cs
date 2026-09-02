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
}