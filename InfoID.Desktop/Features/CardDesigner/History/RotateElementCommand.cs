using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.History;

public sealed class RotateElementCommand : IDesignCommand
{
    private readonly DesignerElement _element;
    private readonly double _before;
    private readonly double _after;

    public RotateElementCommand(DesignerElement element, double before, double after)
    {
        _element = element;
        _before = before;
        _after = after;
    }

    public string Description => $"Rotate {_element.Name}";
    public void Do() => _element.Rotation = _after;
    public void Undo() => _element.Rotation = _before;
}