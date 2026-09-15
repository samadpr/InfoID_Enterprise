using System;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>Thrown for any .infoid import/export failure with a message already safe to
/// show directly to the user (Part 68: no raw stack traces, user-friendly messages).</summary>
public sealed class InfoIdFileException : Exception
{
    public InfoIdFileException(string message) : base(message)
    {
    }

    public InfoIdFileException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
