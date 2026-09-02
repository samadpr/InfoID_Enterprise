namespace InfoID.Desktop.Features.CardDesigner.History;

/// <summary>
/// Command-based undo/redo unit (Part 31) -- each command stores only what it needs to
/// reverse itself (e.g. an element's old/new position), not a snapshot of the whole
/// document, so history stays cheap even with 50+ elements.
/// </summary>
public interface IDesignCommand
{
    string Description { get; }
    void Do();
    void Undo();
}