using System;
using System.Collections.Generic;
using System.Text;

namespace InfoID.Desktop.Features.Database.Models;

public sealed class ExcelPreviewRow
{
    public Dictionary<string, object?> Values { get; } = new();
}
