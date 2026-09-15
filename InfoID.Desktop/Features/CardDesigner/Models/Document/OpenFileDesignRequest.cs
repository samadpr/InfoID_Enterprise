namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

/// <summary>Navigation parameter for opening a file-backed .infoid design (Recent Cards
/// -> a design that was imported rather than saved to the database). Distinct from
/// passing a bare `long` templateId or the various "create from X" parameter types
/// CardDesignerViewModel.BuildDocumentFromNavigationParameter already handles.</summary>
public sealed record OpenFileDesignRequest(string FilePath);
