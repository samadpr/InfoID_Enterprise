namespace InfoID.LicenseManager.Domain.Exceptions;

/// <summary>A licensing rule was violated (for example extending a perpetual license). The message
/// is written for staff and is safe to show; the application layer turns it into a failed result.</summary>
public sealed class DomainRuleException : Exception
{
    public DomainRuleException(string message) : base(message)
    {
    }
}
