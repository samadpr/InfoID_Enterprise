using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

public sealed class DesignClipboard : IDesignClipboard
{
    private string? _serializedElements;

    public bool HasContent => _serializedElements is not null;

    public void SetContent(IReadOnlyList<DesignerElement> elements)
    {
        _serializedElements = JsonSerializer.Serialize(elements.ToList());
    }

    public IReadOnlyList<DesignerElement> GetClones()
    {
        if (_serializedElements is null) return Array.Empty<DesignerElement>();

        var clones = JsonSerializer.Deserialize<List<DesignerElement>>(_serializedElements) ?? new List<DesignerElement>();
        foreach (var clone in clones)
        {
            clone.Id = Guid.NewGuid().ToString("N");
        }
        return clones;
    }
}