using System;
using System.Collections.Generic;
using InfoID.Application.Interfaces;
using InfoID.Licensing.Core;

namespace InfoID.Desktop.Features.Licensing.Services;

/// <summary>
/// The serial-key secret(s) this InfoID build accepts. MUST be the same value as the
/// vendor's INFOID_LICENSE_SECRET_V1 used by the License Manager.
///
/// HOW TO SET IT (before building a release for customers):
///   1. On a PC that has INFOID_LICENSE_SECRET_V1 set, run in PowerShell:
///      $s=[Convert]::FromBase64String($env:INFOID_LICENSE_SECRET_V1); $m=[byte[]](0x5A,0xC3,0x17,0x9E,0x42,0xB8,0x6D,0x01); for($i=0;$i -lt $s.Length;$i++){$s[$i]=$s[$i] -bxor $m[$i % 8]}; ($s | % { '0x{0:X2}' -f $_ }) -join ', ' | Set-Clipboard
///   2. Paste the result (now on the clipboard) between the braces of MaskedSecretV1 below.
///
/// The secret is stored XOR-masked so it isn't visible as plain text in the program
/// file. This is obfuscation, not real protection -- see SerialKey's remarks on the
/// limits of offline 20-character keys.
///
/// DEBUG builds also accept INFOID_LICENSE_SECRET_V1 from the environment, so developers
/// can test before the masked value is filled in. Release builds never read it.
/// </summary>
public sealed class EmbeddedLicenseSecretProvider : ILicenseSecretProvider
{
    private static readonly byte[] Mask = { 0x5A, 0xC3, 0x17, 0x9E, 0x42, 0xB8, 0x6D, 0x01 };

    // Paste the masked bytes for secret version 1 here (32 values).
    private static readonly byte[] MaskedSecretV1 = { };

    /// <summary>
    /// PUBLIC key that verifies ONLINE licenses (contents of license-signing-public.pem,
    /// created on the server with: dotnet run --project InfoID.LicenseApi -- --new-signing-key).
    /// A public key can only check signatures, never create them, so it's safe to embed
    /// as plain text. Paste the whole PEM, including the BEGIN/END lines.
    /// DEBUG builds may instead read it from the file named in INFOID_LICENSE_PUBLIC_KEY_FILE.
    ///
    /// Paste it as a raw string, e.g.:
    ///   private const string SigningPublicKeyPem = """
    ///       -----BEGIN PUBLIC KEY-----
    ///       MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE...
    ///       -----END PUBLIC KEY-----
    ///       """;
    /// </summary>
    private const string SigningPublicKeyPem = "";

    private readonly Lazy<IReadOnlyList<LicenseSecret>> _secrets = new(Load);

    public IReadOnlyList<LicenseSecret> GetSecrets() => _secrets.Value;

    public string? GetSigningPublicKeyPem()
    {
        if (!string.IsNullOrWhiteSpace(SigningPublicKeyPem))
            return SigningPublicKeyPem;

#if DEBUG
        var file = Environment.GetEnvironmentVariable("INFOID_LICENSE_PUBLIC_KEY_FILE");
        if (!string.IsNullOrWhiteSpace(file) && System.IO.File.Exists(file))
            return System.IO.File.ReadAllText(file);
#endif
        return null;
    }

    private static IReadOnlyList<LicenseSecret> Load()
    {
        var secrets = new List<LicenseSecret>();

        if (MaskedSecretV1.Length >= LicenseSecret.MinKeyLength)
            secrets.Add(new LicenseSecret(1, Unmask(MaskedSecretV1)));

#if DEBUG
        if (secrets.Count == 0)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("INFOID_LICENSE_SECRET_V1");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                secrets.Add(LicenseSecret.FromBase64(1, fromEnvironment));
        }
#endif

        return secrets;
    }

    private static byte[] Unmask(byte[] masked)
    {
        var result = new byte[masked.Length];
        for (var i = 0; i < masked.Length; i++)
            result[i] = (byte)(masked[i] ^ Mask[i % Mask.Length]);
        return result;
    }
}
