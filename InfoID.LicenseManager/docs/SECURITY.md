# Security

## Controls in place

| Area | Control | Test |
|---|---|---|
| Authentication | Cookie `InfoID.Admin`: HttpOnly, SameSite=Strict, Secure outside development, 2 h sliding expiry. Every request re-checks the account in the database (disabled/deleted/role changed users are signed out at once) | `AuthenticationFlowTests`, `SecurityAndErrorTests` |
| Passwords | PBKDF2 (format `PBKDF2$iterations$salt$hash`), constant-time compare, generic "invalid e-mail or password" message, equalised timing for unknown users | `AuthenticationFlowTests`, service tests |
| Brute force | Account lockout (5 failures / 15 min) and per-address rate limit on sign-in/setup (10 per minute) | `LoginRateLimitTests`, `HardeningTests` |
| Authorisation | Fallback policy: everything needs sign-in unless marked `[AllowAnonymous]`; `ManageLicenses` and `SuperAdmin` policies on actions; services re-check the role through `ICurrentUser` | Per-role 403 tests on every page |
| CSRF | Global `AutoValidateAntiforgeryToken` on all POSTs; anti-forgery cookie Secure outside development; state-changing actions are POST-only | Missing-token and GET tests on every action |
| IDOR / over-posting | Ids bound from the route only; sub-forms bound with explicit prefixes; ViewModels expose only editable fields; privileged fields (approval terms, roles) are never taken from a plain form | Tests post a rival `id` and extra fields |
| XSS | Razor encoding everywhere, no `Html.Raw` on user data, **no inline script or style** so CSP forbids both | Encoding tests on every list/detail page |
| Headers | CSP (`default-src 'self'`, `frame-ancestors 'none'`, `form-action 'self'`), `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, COOP/CORP `same-origin`, `Cache-Control: no-store`, HSTS + HTTPS redirect outside development, no `Server` header | `SecurityAndErrorTests`, `HardeningTests` |
| Reverse proxy | `X-Forwarded-*` honoured only from addresses in `Proxy:KnownProxies`; otherwise ignored, so the rate limiter and audit log cannot be tricked | `HardeningTests` |
| Open redirect | Sign-in return URL accepted only if local | `AuthenticationFlowTests` |
| Errors | Staff site: generic page + trace id. API: JSON 500 + trace id. No stack traces, SQL or secrets outside development | `SecurityAndErrorTests`, `ClientApiTests` |
| Secrets | Read only from environment/secret store; startup warnings if missing; never written to logs, audit rows or error responses (log capture tests); connection string has no password in the repo | `ClientApiTests` (log capture), `ConfigurationLicenseSecretProviderTests` |
| Concurrency | Row locks / atomic updates for activation slots, approvals, trial limits; advisory locks for the audit chain and first admin; verified against PostgreSQL | `Integration/*` |
| Audit | Every staff mutation writes a hash-chained audit row in the same transaction; the Audit page can verify the chain | `AuditPageTests`, domain tests |
| Request size | 1 MB request body limit; client API rate limits per address | Configuration |

## Operator checklist

- [ ] `ASPNETCORE_ENVIRONMENT=Production`.
- [ ] HTTPS only; certificate valid; HSTS is sent by the app (30 days by default) - lengthen it at the proxy once you are sure.
- [ ] `INFOID_LICENSE_DB` set from a secret store; database role has only the rights listed in [DATABASE.md](DATABASE.md).
- [ ] `INFOID_LICENSE_SIGNING_KEY_FILE` points outside the web root; file readable by the service account only.
- [ ] `INFOID_LICENSE_SECRET_V1` set (same values as the legacy API if replacing it).
- [ ] `DataProtection__KeysPath` set to a persistent folder with restricted access.
- [ ] `AllowedHosts` set to the real host name.
- [ ] `Proxy__KnownProxies__*` set to the proxy addresses (and the proxy overwrites, not appends, `X-Forwarded-For`).
- [ ] SMTP configured; test with a verification e-mail.
- [ ] First Super Admin created, strong unique password; other staff given the lowest role that fits.
- [ ] Nightly database backup; key material backed up separately; restore tested.
- [ ] Verify the audit chain after maintenance.

## Findings from the review of the migration (and what was done)

| Finding | Severity | Status |
|---|---|---|
| Legacy `InfoID.AdminLicenseManager/appsettings.json` and `InfoID.LicenseApi/appsettings.json` contain a PostgreSQL **password** and are tracked by git | High | Not changed by this migration (legacy projects are preserved). **Rotate that password**, then set the new one through `INFOID_LICENSE_DB`/user-secrets. The new site never stores a password in a file |
| A posted form field called `id` overrode the route id (record substitution) | High | Fixed (`[FromRoute]`), regression tests on every id action |
| Error pages re-executed POSTs and failed the anti-forgery check | Medium | Fixed (`ErrorController` ignores anti-forgery) |
| Cookie/anti-forgery keys were stored in the user profile only | Medium | `DataProtection:KeysPath` added; startup warning in production when absent |
| Behind a proxy every caller shared one address (rate limit, audit) | Medium | `Proxy:KnownProxies` added; forwarded headers trusted only from listed proxies |
| Culture-dependent number formatting in validation messages ("1,02,300") | Low | Fixed (invariant culture) in the new code and in `InfoID.Licensing.Core` |
| Missing `Permissions-Policy` / cross-origin isolation headers, `Server` header advertised, 30 MB body limit | Low | Fixed |
| No second factor / SSO for staff | Info | Not implemented (see limitations in [DEPLOYMENT.md](DEPLOYMENT.md)) |
| Rate limiter is in-memory per instance | Info | Documented |

## Reporting a vulnerability

Do not file a public issue. Send the details to the operator of the installation (the address configured in `INFOID_SALES_NOTIFY_EMAIL` is for customer requests only).
