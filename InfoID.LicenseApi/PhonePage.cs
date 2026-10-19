using System.Net;
using InfoID.Licensing.Core;

namespace InfoID.LicenseApi;

/// <summary>The small mobile page shown after scanning InfoID's offline-activation QR
/// code: the serial key in large, easy-to-read letters, or a clear error.</summary>
internal static class PhonePage
{
    public static string Render(string? serialKey = null, SerialKeyTerms? terms = null, string? error = null)
    {
        var body = serialKey is not null
            ? $"""
               <p class="ok">Your InfoID serial key</p>
               <div class="key">{WebUtility.HtmlEncode(serialKey)}</div>
               <p>Type this key into InfoID on your computer and click <b>Activate</b>.</p>
               <p class="small">{WebUtility.HtmlEncode(Describe(terms))}</p>
               <p class="small">This key works only on the computer that showed the QR code.</p>
               """
            : $"""
               <p class="err">Activation not possible</p>
               <p>{WebUtility.HtmlEncode(error ?? "Unknown error.")}</p>
               <p class="small">If you need help, contact your InfoID vendor.</p>
               """;

        return $$"""
            <!doctype html>
            <html lang="en"><head>
            <meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>InfoID activation</title>
            <style>
              body{font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;margin:0;padding:24px;background:#16171b;color:#f2f2f2}
              .card{max-width:520px;margin:0 auto;background:#23252b;border-radius:14px;padding:24px}
              h1{font-size:20px;margin:0 0 12px}
              .key{font-family:Consolas,Menlo,monospace;font-size:26px;letter-spacing:2px;background:#101114;border-radius:10px;padding:16px;text-align:center;word-break:break-all;user-select:all}
              .ok{color:#3fbf6a;font-weight:600} .err{color:#ff6b61;font-weight:600} .small{font-size:13px;opacity:.7}
            </style></head>
            <body><div class="card"><h1>InfoID</h1>{{body}}</div></body></html>
            """;
    }

    private static string Describe(SerialKeyTerms? terms) => terms is null ? "" :
        $"{terms.Tier} license" +
        (terms.ExpiryDate is { } e ? $", valid until {e:dd MMM yyyy}" : ", no expiry") +
        (terms.MaxCardsPerPeriod is { } m ? $", {m:N0} cards per {terms.LimitPeriod.ToString().ToLowerInvariant()[..^2]}." : ", unlimited cards.");
}
