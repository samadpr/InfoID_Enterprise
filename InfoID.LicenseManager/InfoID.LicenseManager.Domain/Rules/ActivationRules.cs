using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Domain.Rules;

/// <summary>One reason a key or token cannot (or should not) be issued. Advisory problems are shown
/// to staff but do not stop issuing.</summary>
public sealed record ActivationProblem(string Message, bool IsBlocking = true);

/// <summary>
/// The checks every activation path shares (staff key, phone page, online activation): the code
/// exists and is not revoked, a machine slot is free (re-activating the same machine never uses a
/// new one), the license has not expired, the machine/code is not blacklisted, and the request is
/// fresh. Wording and thresholds are those of the desktop License Manager.
/// </summary>
public static class ActivationRules
{
    public const int RequestCodeMaxAgeDays = 30;

    public static IReadOnlyList<ActivationProblem> Evaluate(
        LicenseCode code, string fingerprintHex, bool isBlacklisted, DateOnly requestDate, DateOnly today)
    {
        var problems = new List<ActivationProblem>();
        var active = code.ActiveActivations.ToList();
        var sameMachine = active.Any(a => a.MachineFingerprint == fingerprintHex);

        if (code.IsRevoked)
            problems.Add(new("This activation code has been revoked."));
        if (!sameMachine && active.Count >= code.MaxActivations)
            problems.Add(new($"All {code.MaxActivations} activation slot(s) are in use. The customer must deactivate the old machine first, or use Transfer."));
        if (code.ExpiryDate is { } expiry && expiry < today)
            problems.Add(new($"This license expired on {expiry:d}. Extend it before issuing a key."));
        if (isBlacklisted)
            problems.Add(new("This machine or activation code is blacklisted."));
        if (requestDate < today.AddDays(-RequestCodeMaxAgeDays))
            problems.Add(new($"This request code is from {requestDate:d}, more than {RequestCodeMaxAgeDays} days ago. Ask the customer for a new one if in doubt.", IsBlocking: false));

        return problems;
    }

    public static bool IsSameMachineActive(LicenseCode code, string fingerprintHex) =>
        code.ActiveActivations.Any(a => a.MachineFingerprint == fingerprintHex);
}
