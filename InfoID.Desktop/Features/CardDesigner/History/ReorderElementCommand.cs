using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.History;

/// <summary>Z-order change -- Bring to Front / Send to Back / Bring Forward / Send Backward (Part 43).</summary>
public sealed class ReorderElementCommand : IDesignCommand
{
    private readonly DesignerElement _element;
    private readonly int _before;
    private readonly int _after;

    public ReorderElementCommand(DesignerElement element, int before, int after)
    {
        _element = element;
        _before = before;
        _after = after;
    }

    public string Description => $"Reorder {_element.Name}";
    public void Do() => _element.ZIndex = _after;
    public void Undo() => _element.ZIndex = _before;
}