using InfoID.Domain.Common;
using InfoID.Domain.Common.Enums;

namespace InfoID.Domain.Entities.SystemModule;

/// <summary>
/// A registered third-party/internal plugin: custom field type, printer, or integration adapter (FR-MOD-3).
/// </summary>
public class PluginRegistration : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public PluginType PluginType { get; set; }  // FieldType / Printer / Integration
    public string? Version { get; set; }
    public string? AssemblyPath { get; set; }
    public bool IsEnabled { get; set; }  // Default 1
}
