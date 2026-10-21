# InfoID License Manager (web)

ASP.NET Core MVC replacement for the Avalonia **InfoID.AdminLicenseManager** and the **InfoID.LicenseApi**.
One site serves two audiences:

* **Staff** (browser, cookie sign-in): dashboard, license codes, serial keys, plan requests, customers, plans, audit log, activation simulator.
* **InfoID.Desktop** (JSON API, bearer tokens): online activation, check-in, deactivation, accounts, trial printing, plans, requests, and the `/a` phone/QR activation page.

| Document | Read it to |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Understand the layers, dependency rules and the main design decisions |
| [docs/DATABASE.md](docs/DATABASE.md) | Configure PostgreSQL, the connection string and first-run seeding |
| [docs/MIGRATIONS.md](docs/MIGRATIONS.md) | Apply, add and review EF Core migrations |
| [docs/TESTING.md](docs/TESTING.md) | Run the tests, including the PostgreSQL ones |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Configure secrets, keys, e-mail, proxy and go live |
| [docs/SECURITY.md](docs/SECURITY.md) | See the controls in place and the operator checklist |

## Quick start (development)

```bash
# 1. Create an empty database called LicenseDb in PostgreSQL, then keep the password out of the repository:
dotnet user-secrets set "ConnectionStrings:LicenseDb" "Host=localhost;Port=5432;Database=LicenseDb;Username=postgres;Password=<your password>" --project InfoID.LicenseManager.Web

# 2. One-time keys (the private key must stay outside the repository):
dotnet run --project InfoID.LicenseManager.Web -- --new-signing-key     # writes license-signing-private.pem / -public.pem in the current folder
dotnet run --project InfoID.LicenseManager.Web -- --new-secret          # prints a value for INFOID_LICENSE_SECRET_V1

# 3. Run (the first start creates the tables and the default plans):
dotnet run --project InfoID.LicenseManager.Web --launch-profile https
```

Open <https://localhost:7143>. While no staff account exists the site shows **/Account/Setup** to create the first Super Admin.
Details for every variable are in [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

## Solution layout

```
InfoID.LicenseManager.Domain          entities, rules, value objects (no dependencies except InfoID.Licensing.Core)
InfoID.LicenseManager.Application     use cases: services, DTOs, validators, repository/security/notification interfaces
InfoID.LicenseManager.Infrastructure  EF Core (PostgreSQL), repositories, unit of work, hashing, signing, SMTP
InfoID.LicenseManager.Web             controllers, API controllers, ViewModels, Razor views, middleware, composition root
InfoID.LicenseManager.Tests           unit, web (in-memory) and PostgreSQL integration tests
```

The old Avalonia admin app, `InfoID.LicenseApi` and `InfoID.Licensing.Server` are still in the solution until the operator confirms the web site
has replaced them (see `MIGRATION_TRACKER.md` in the solution root).
