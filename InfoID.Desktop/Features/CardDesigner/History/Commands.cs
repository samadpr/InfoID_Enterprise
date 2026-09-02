using System;
using System.Collections.Generic;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.History;

/// <summary>Adds one element to a side's element list.</summary>
public sealed class AddElementCommand : IDesignCommand
{
    private readonly CardDesignSide _side;
    private readonly DesignerElement _element;

    public AddElementCommand(CardDesignSide side, DesignerElement element)
    {
        _side = side;
        _element = element;
    }

    public string Description => $"Add {_element.Name}";
    public void Do() => _side.Elements.Add(_element);
    public void Undo() => _side.Elements.Remove(_element);
}

/// <summary>Removes one or more elements from a side.</summary>
public sealed class DeleteElementsCommand : IDesignCommand
{
    private readonly CardDesignSide _side;
    private readonly List<DesignerElement> _elements;

    public DeleteElementsCommand(CardDesignSide side, IEnumerable<DesignerElement> elements)
    {
        _side = side;
        _elements = new List<DesignerElement>(elements);
    }

    public string Description => _elements.Count == 1 ? $"Delete {_elements[0].Name}" : $"Delete {_elements.Count} elements";
    public void Do() { foreach (var e in _elements) _side.Elements.Remove(e); }
    public void Undo() { foreach (var e in _elements) _side.Elements.Add(e); }
}

/// <summary>Moves/resizes one element -- captures before/after rectangles so drag and
/// resize interactions collapse into a single undo step rather than one per pixel.</summary>
public sealed class TransformElementCommand : IDesignCommand
{
    private readonly DesignerElement _element;
    private readonly (double X, double Y, double Width, double Height) _before;
    private readonly (double X, double Y, double Width, double Height) _after;

    public TransformElementCommand(
        DesignerElement element,
        (double X, double Y, double Width, double Height) before,
        (double X, double Y, double Width, double Height) after)
    {
        _element = element;
        _before = before;
        _after = after;
    }

    public string Description => $"Move/resize {_element.Name}";

    public void Do()
    {
        _element.X = _after.X; _element.Y = _after.Y;
        _element.Width = _after.Width; _element.Height = _after.Height;
    }

    public void Undo()
    {
        _element.X = _before.X; _element.Y = _before.Y;
        _element.Width = _before.Width; _element.Height = _before.Height;
    }
}

/// <summary>Generic single-property change (text content, color, font size, ...) --
/// reused by the properties panel so every editable field gets undo for free.</summary>
public sealed class ChangePropertyCommand<TElement, TValue> : IDesignCommand
    where TElement : DesignerElement
{
    private readonly TElement _element;
    private readonly Action<TElement, TValue> _setter;
    private readonly TValue _before;
    private readonly TValue _after;
    public string Description { get; }

    public ChangePropertyCommand(TElement element, Action<TElement, TValue> setter, TValue before, TValue after, string description)
    {
        _element = element;
        _setter = setter;
        _before = before;
        _after = after;
        Description = description;
    }

    public void Do() => _setter(_element, _after);
    public void Undo() => _setter(_element, _before);
}