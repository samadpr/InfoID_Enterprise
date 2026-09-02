using System.Collections.Generic;

namespace InfoID.Desktop.Features.CardDesigner.History;

/// <summary>Per-document undo/redo stack (Part 31: "History should be per design tab").</summary>
public sealed class DesignHistory
{
    private readonly Stack<IDesignCommand> _undoStack = new();
    private readonly Stack<IDesignCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Execute(IDesignCommand command)
    {
        command.Do();
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    /// <summary>Record a command that already happened (e.g. a drag that's already
    /// applied live to the model) without re-running Do().</summary>
    public void Record(IDesignCommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var command = _redoStack.Pop();
        command.Do();
        _undoStack.Push(command);
    }
}