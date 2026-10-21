# Deployment guide

## 1. Prerequisites

* .NET 10 runtime (ASP.NET Core) on the server.
* PostgreSQL 14 or newer (tested on 18) reachable from the server, with an empty database ([DATABASE.md](DATABASE.md)).
* A host name and a TLS certificate. The site must be served over HTTPS (cookies are `Secure` outside development). A reverse proxy (IIS/ARR, nginx, Caddy, a cloud load balancer) that terminates TLS is fine.
* An SMTP account for customer e-mail (verification codes, activation codes). Without one, mails are written as text files to an outbox folder - fine for testing, **not** for production.

## 2. Create the secrets (once)

| Secret | How to create it | Where it goes | If it is lost |
|---|---|---|---|
| ECDSA signing key pair | `dotnet InfoID.LicenseManager.Web.dll --new-signing-key` writes `license-signing-private.pem` and `license-signing-public.pem` to the current folder | Private key: **server only**, outside the web root and outside source control, path in `INFOID_LICENSE_SIGNING_KEY_FILE` (or the PEM text in `INFOID_LICENSE_SIGNING_KEY`). Public key: embedded in the InfoID desktop build | Online activation tokens can no longer be issued/verified; a new pair needs a new desktop release |
| Serial-key secret | `dotnet InfoID.LicenseManager.Web.dll --new-secret` prints a base64 value | `INFOID_LICENSE_SECRET_V1` on the server, and the same value in the desktop build (`EmbeddedLicenseSecretProvider`) | Existing offline serial keys stop validating |

Rotation: add `INFOID_LICENSE_SECRET_V2` (up to `V15`); new keys use the highest version, old versions keep validating keys already issued. Never reuse or remove a version that issued keys are still using.

If you are replacing the legacy License API, **reuse its existing signing key and secrets** so installed InfoID apps keep working.

## 3. Configuration reference

Set as environment variables (ASP.NET Core also accepts `Section__Key` for configuration keys) or in the host's secret store. Do not store secrets in `appsettings.json`.

| Variable / key | Required | Purpose |
|---|---|---|
| `INFOID_LICENSE_DB` or `ConnectionStrings__LicenseDb` | Yes | PostgreSQL connection string |
| `INFOID_LICENSE_SIGNING_KEY_FILE` / `INFOID_LICENSE_SIGNING_KEY` | Yes for online activation | ECDSA private key (file path or PEM text) |
| `INFOID_LICENSE_SECRET_V1` (`..V15`) | Yes for serial keys and the phone/QR page | HMAC secrets |
| `INFOID_SMTP_HOST`, `INFOID_SMTP_PORT` (587), `INFOID_SMTP_USER`, `INFOID_SMTP_PASSWORD`, `INFOID_SMTP_FROM`, `INFOID_SMTP_SSL` (`false` to disable) | Production | Outgoing mail |
| `EmailSettings` section (`Host`, `Port`, `Username`, `Password`, `From`, `FromName`, `EnableSsl`) | Production | Same as the `INFOID_SMTP_*` variables, which take priority when both are present. Put the password in user-secrets (`EmailSettings:Password`) or an environment variable (`EmailSettings__Password`), not in a committed file. Port 587 with STARTTLS (default); port 465 (implicit TLS) is not supported. Gmail/Microsoft 365 need an app password. The startup log says whether real delivery or the outbox is active |
| `INFOID_MAIL_OUTBOX` | No | Folder for the no-SMTP outbox (default `mail-outbox` next to the app) |
| `INFOID_SALES_NOTIFY_EMAIL` | No | Address that is e-mailed when a customer asks for a plan |
| `Database__MigrateOnStartup` | No (default `false`) | Apply migrations and seed default plans at startup |
| `DataProtection__KeysPath` | **Yes in production** | Persistent, access-restricted folder for the cookie/anti-forgery keys. Without it everyone is signed out and open forms fail whenever the key store is lost (container restart, new app-pool identity). Share the same folder between instances |
| `Proxy__KnownProxies__0`, `__1`... | When behind a proxy | IP addresses of your reverse proxies. Their `X-Forwarded-For` / `X-Forwarded-Proto` are honoured; from anyone else they are ignored |
| `AllowedHosts` | Recommended | Real host name(s), e.g. `licenses.example.com`. The default `*` produces a startup warning in production |
| `Trial__CardLimit`, `Trial__ReservationMinutes`, `Trial__MaxCardsPerReservation` | No | Trial printing limits (defaults 10 / 30 / 10) |
| `ASPNETCORE_ENVIRONMENT` | Yes | `Production` on the server (anything else enables the developer error page) |
| `ASPNETCORE_URLS` | If not using IIS | e.g. `http://127.0.0.1:5000` behind a proxy |

On startup the site logs a warning for each missing secret or risky setting (no signing key, no serial secret, no key path, wildcard host). Read the first lines of the log after every deployment.

## 4. Publish and run

```bash
dotnet publish InfoID.LicenseManager/InfoID.LicenseManager.Web -c Release -o ./publish
```

Run it as a service:

* **Windows**: IIS with the ASP.NET Core Hosting Bundle (set the variables in the application pool's environment or `web.config`'s `environmentVariables`), or `sc create` / NSSM around `dotnet InfoID.LicenseManager.Web.dll`.
* **Linux**: a `systemd` unit with `Environment=`/`EnvironmentFile=` (file readable only by the service account), behind nginx or Caddy.

Example nginx location (the proxy address must be listed in `Proxy__KnownProxies__0`):

```
location / {
    proxy_pass         http://127.0.0.1:5000;
    proxy_set_header   Host $host;
    proxy_set_header   X-Forwarded-For $remote_addr;
    proxy_set_header   X-Forwarded-Proto $scheme;
}
```

Use `$remote_addr` (not `$proxy_add_x_forwarded_for`) so a client cannot prepend an address of its own choosing.

## 5. First start

1. Set `Database__MigrateOnStartup=true` for the first start (or run the migrations as in [MIGRATIONS.md](MIGRATIONS.md)). Turn it off afterwards if you prefer pipeline-driven migrations.
2. Check `GET /health` returns `{"status":"ok"}`.
3. Open the site. With an empty `AdminUsers` table it shows **/Account/Setup**: create the first Super Admin immediately (anyone who reaches the site first can claim it, so do this before exposing the site publicly, or keep it on an internal address until done). The page disappears once an admin exists.
4. Sign in, open **Plans** and review the three seeded plans and their prices text.
5. Create the other staff accounts with the least privilege needed: `LicenseManager` (create/revoke/approve), `SupportAgent` (view, generate offline keys, resend verification).

## 6. Point InfoID.Desktop at the new site

The desktop client reads the server address from `INFOID_LICENSE_SERVER_URL` (environment variable on the user's PC), otherwise from `LicenseServerSettings.ProductionUrl` compiled into the build (the Debug default is the legacy API at `https://localhost:7043`). For production set `ProductionUrl` to your HTTPS address (without trailing slash) in the release build; for a test PC set the environment variable to `https://<host>:7143` (the development profile).

The activation request code QR (`/a?c=...`) opens in the customer's phone browser, so the site must be reachable from that phone. During development the launch profile listens on `http://0.0.0.0:5143` for that reason; allow it in the Windows firewall for the private network only. In production this is simply your public HTTPS address.

## 7. Cut-over from the legacy applications

1. Run the new site against a **copy** of the production database first; sign in, search codes, approve a test request, activate a test machine from InfoID.Desktop.
2. Use the same signing key and the same `INFOID_LICENSE_SECRET_V*` values as the legacy API.
3. Stop the legacy License API and Admin app, start the new site on the production database, point the desktop builds at it.
4. Keep the legacy projects in source control until a release cycle has passed without problems, then remove them (this is a separate, deliberate step - see `MIGRATION_TRACKER.md`).

## 8. Monitoring and operations

* **Health**: `GET /health` (anonymous, no details). Use it for load balancer and uptime checks.
* **Logs**: one line per request (method, path without the query string, status, duration, caller address; no bodies). The trace id shown on the error page / API 500 response identifies the failing request in the error log entry.
* **Audit log**: **Audit** page shows every staff action; **Verify chain** detects edited or deleted rows. Run it after any manual database work.
* **Backups**: nightly `pg_dump` plus a copy of the key material, stored separately.
* **Updates**: deploy the new build, apply migrations, restart; sessions survive restarts only if `DataProtection__KeysPath` is set.

## 9. Known limitations

* No e-mail templating UI (`EmailTemplates` exists in the schema but is unused).
* Transfers, fraud alerts and support tickets exist in the schema only; there are no pages for them (the legacy desktop app did not have them either).
* Single sign-on and two-factor sign-in for staff are not implemented; protect the site with network rules or the proxy if you need more than password + lockout.
* The in-process rate limiter is per instance; with several instances behind a load balancer the effective limit is multiplied.
