namespace InfoID.Shared.Logging;

/// <summary>Helpers that keep personal data out of technical logs.</summary>
public static class LogText
{
    /// <summary>"john.doe@acme.com" -> "j***@acme.com". Enough to tell support which
    /// customer is affected (with the time), without writing the address into the log.</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(none)";
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        return email[0] + "***" + email[at..];
    }

    /// <summary>Path without the query string: "/a?c=REQUEST-CODE" -> "/a".</summary>
    public static string PathOnly(string? pathAndQuery)
    {
        if (string.IsNullOrEmpty(pathAndQuery)) return "/";
        var q = pathAndQuery.IndexOf('?');
        return q < 0 ? pathAndQuery : pathAndQuery[..q];
    }
}
