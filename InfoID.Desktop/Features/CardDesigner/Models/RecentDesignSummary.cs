using System;

namespace InfoID.Desktop.Features.CardDesigner.Models;

public sealed record RecentDesignSummary(
    long TemplateId,
    string Name,
    string CardFormatLabel,
    DateTime? LastOpenedDate,
    DateTime ModifiedDate,
    bool IsPinned);