using System.Collections.Generic;

namespace InfoID.Desktop.Features.CardDesigner.History;

/// <summary>Bundles several commands (e.g. moving 5 selected elements together) into
/// one undo step.</summary>
public sealed class CompositeCommand : IDesignCommand
{
    private readonly List<IDesignCommand> _commands;
    public string Description { get; }

    public CompositeCommand(string description, List<IDesignCommand> commands)
    {
        Description = description;
        _commands = commands;
    }

    public void Do()
    {
        foreach (var c in _commands) c.Do();
    }

    public void Undo()
    {
        for (var i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo();
    }
}