namespace InfoID.LicenseManager.Application.Common;

/// <summary>The signed-in staff member's role does not allow the operation. Normally the web layer's
/// authorization policies stop such a request first; this is the second, server-side line of defence.
/// Mapped to HTTP 403; the message is for the log, not for the browser.</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }
}

/// <summary>The record a request points at does not exist (stale or guessed id). Mapped to HTTP 404.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string entity, long id) : base($"{entity} {id} was not found.")
    {
    }
}
