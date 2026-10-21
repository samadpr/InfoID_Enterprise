# Database configuration

The site uses **PostgreSQL** (developed and tested on PostgreSQL 18) through EF Core 10 and Npgsql.

## Connection string

The site looks, in this order, at:

1. environment variable `INFOID_LICENSE_DB` (preferred in production: the password never touches a file),
2. configuration key `ConnectionStrings:LicenseDb` (use **user-secrets** in development, or `ConnectionStrings__LicenseDb` as an environment variable).

If neither is set the site refuses to start with a message saying so.

`appsettings.json` ships only a password-less default (`Host=localhost;Port=5432;Database=LicenseDb;Username=postgres`). Never put a password in a file that is committed.

```bash
# development
dotnet user-secrets set "ConnectionStrings:LicenseDb" "Host=localhost;Port=5432;Database=LicenseDb;Username=postgres;Password=<password>" --project InfoID.LicenseManager.Web
```

```powershell
# production (Windows service / IIS: set it in the service or app-pool environment)
[Environment]::SetEnvironmentVariable("INFOID_LICENSE_DB", "Host=db.internal;Database=LicenseDb;Username=infoid_app;Password=...;SSL Mode=Require", "Machine")
```

Use a dedicated database role for the site rather than `postgres`. It needs `CONNECT` on the database and, in the schema the tables live in, `CREATE` (only if the site applies migrations itself) plus `SELECT, INSERT, UPDATE, DELETE` on the tables and `USAGE` on the sequences. The role must be allowed to take advisory locks (this is the default).

## Creating the database

Create an empty database once (the site creates the tables, it does not create the database):

```sql
CREATE DATABASE "LicenseDb";
```

Then either let the site migrate on startup (see below) or run the migrations yourself ([MIGRATIONS.md](MIGRATIONS.md)).

## Startup behaviour

| Setting | Default | Effect |
|---|---|---|
| `Database:MigrateOnStartup` | `false` | `true` applies pending migrations and then creates the InfoID product and the three default plans (Standard, Professional, Enterprise) if no plan exists. Safe to leave on for a single instance; with several instances prefer running migrations from the deployment pipeline |

Seeding never overwrites or re-creates plans staff already changed.

## What lives where

| Area | Tables (via `LicenseManagerDbContext`) |
|---|---|
| Staff | `AdminUsers`, `AdminAuditLogs` (the hash-chained audit log) |
| Licensing | `Products`, `Plans`, `LicenseCodes`, `ActivationRecords`, `CustomerRecords`, `BlacklistEntries`, `TransferHistories`, `FraudAlerts`, `SupportTickets`, `EmailTemplates` |
| Customers (app sign-ups) | `CustomerAccounts`, `CustomerSessions`, `EmailCodes`, `LicenseRequests`, `TrialUsages`, `TrialReservations` |

(`TransferHistories`, `FraudAlerts`, `SupportTickets` and `EmailTemplates` exist in the schema for compatibility; no page uses them yet.)

The exact schema is the migration history in `InfoID.LicenseManager.Infrastructure/Persistence/Migrations`; it is identical to the one created by the previous `InfoID.Licensing.Server`, so an existing database can be pointed at by the new site without conversion.

## Sharing a database with the legacy applications

The old Admin app and License API use the same schema. They can run against the same database while you verify the web site, but do not run both a legacy and a new instance that *migrate* at startup at the same time.

## Backups

Back up the database with `pg_dump` (the audit chain and activation records are the valuable parts). Also keep, **separately and securely**: the ECDSA private key and the `INFOID_LICENSE_SECRET_V*` values. Without them, serial keys and tokens already issued cannot be verified or reissued.
