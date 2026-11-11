namespace InfoID.Application.Services;

/// <summary>One spelling for tag UIDs everywhere: hex digits only, upper case ("04A1B2C3D4E580").</summary>
public static class NfcUid
{
    public static string Normalize(string? uid) =>
        uid is null ? string.Empty : new string(uid.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
}
