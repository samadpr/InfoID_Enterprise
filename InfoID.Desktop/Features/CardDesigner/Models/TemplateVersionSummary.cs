using System;

namespace InfoID.Desktop.Features.CardDesigner.Models;

public sealed record TemplateVersionSummary(long VersionId, int VersionNumber, DateTime CreatedDate, string? ChangeNote);