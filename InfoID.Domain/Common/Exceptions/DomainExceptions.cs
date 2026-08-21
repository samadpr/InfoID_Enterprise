namespace InfoID.Domain.Common.Exceptions;

/// <summary>Base type for exceptions that represent a business rule violation,
/// as opposed to a bug or infrastructure failure. Application services throw
/// these; the UI layer catches DomainException specifically to show a clean
/// message instead of a stack trace.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

public class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string entityName, long id)
        : base($"{entityName} with Id {id} was not found.") { }
}

public class DuplicateEntityException : DomainException
{
    public DuplicateEntityException(string entityName, string field, string value)
        : base($"{entityName} with {field} '{value}' already exists.") { }
}

/// <summary>Thrown by licensed-feature checks (issuance, printing, encoding) --
/// per FR-LIC-17 this must never block login or general app use, only the
/// specific gated action.</summary>
public class LicenseExpiredException : DomainException
{
    public LicenseExpiredException(string feature)
        : base($"Your license does not permit '{feature}'. Renew or upgrade your license to continue.") { }
}
