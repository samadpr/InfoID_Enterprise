# EF Core migrations

Migrations live in `InfoID.LicenseManager.Infrastructure/Persistence/Migrations`. The four that existed in the legacy server were copied with their **original ids**, so a database created by the old applications already has the right `__EFMigrationsHistory`:

```
20260928132735_InitialCreate
20260930093550_AddOnlineActivation
20261001100805_AddCustomerPlatform
20261012085219_AddTrialUsage
```

Tests prove that the current model equals the snapshot (no pending model change) and that the generated SQL is identical to the legacy migrations.

## Tooling

```bash
dotnet tool install --global dotnet-ef        # once per machine
```

`dotnet ef` builds the context through `DesignTimeDbContextFactory` in the Infrastructure project, so the commands need only the Infrastructure project and the connection string in `INFOID_LICENSE_DB` (commands that do not connect, like `migrations add`, work without it).

## Apply migrations

Pick one:

1. **On startup** - set `Database:MigrateOnStartup=true` (environment variable `Database__MigrateOnStartup=true`). Good for development and single-instance deployments.
2. **From the command line** (recommended for production, run by the pipeline before the new version starts):

   ```powershell
   $env:INFOID_LICENSE_DB = "Host=...;Database=LicenseDb;Username=...;Password=..."
   dotnet ef database update --project InfoID.LicenseManager.Infrastructure
   ```
3. **As a script** for a DBA to review and run:

   ```bash
   dotnet ef migrations script --idempotent --project InfoID.LicenseManager.Infrastructure --output licensemanager.sql
   ```

## Add a migration

1. Change the entity or its configuration (`Persistence/Configurations`).
2. Create it:

   ```bash
   dotnet ef migrations add <Name> --project InfoID.LicenseManager.Infrastructure --output-dir Persistence/Migrations
   ```
3. **Read the generated `Up`/`Down` and the SQL script.** Destructive changes (dropped columns, narrowed types) need an explicit data plan.
4. Run the test suite. `ModelParityTests` fails if the snapshot and the model disagree; the PostgreSQL tests apply every migration to an empty database ([TESTING.md](TESTING.md)).
5. Commit the migration, its `.Designer.cs` and the updated model snapshot together.

Rules of thumb:

* Never edit or delete a migration that has been applied to a shared database; add a new one.
* The client app and the old Admin app still read the same tables. Make changes backward compatible (add nullable columns, then backfill, then tighten) until the legacy applications are retired.

## Roll back

```bash
dotnet ef database update <PreviousMigrationName> --project InfoID.LicenseManager.Infrastructure
dotnet ef migrations remove --project InfoID.LicenseManager.Infrastructure      # only if it was never applied elsewhere
```

Take a `pg_dump` first: a `Down` migration can drop data.
