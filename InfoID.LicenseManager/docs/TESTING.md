# Testing

The suite is in `InfoID.LicenseManager.Tests` (xUnit). Run everything from the solution folder:

```bash
dotnet test InfoID.LicenseManager/InfoID.LicenseManager.Tests
```

## What is covered

| Folder | Kind | Needs a database |
|---|---|---|
| `Unit/Domain` | Entity behaviour and rules: lockout, slots, terms, request lifecycle, plan details, audit hash chain | No |
| `Unit/Application` | Every service through in-memory repositories: role enforcement, validation, audit entries, rollback on failure, e-mail failure handling | No |
| `Web` | The real pipeline (routing, filters, cookies, anti-forgery, views, headers, rate limiter) with in-memory repositories: staff pages, client API contracts, security and hardening | No |
| `Integration` | The EF model against real PostgreSQL: migrations, model/snapshot parity, repository SQL, parallel slot and approval races, trial atomicity, audit chain interop | **Yes** |
| `Integration/CompositionRootTests` | The real service registrations resolve (scope validation on) | No |

Result without PostgreSQL: all tests pass and the 23 PostgreSQL tests are reported as **skipped** (not passed). Always run them before a release.

## Running the PostgreSQL tests

With an external server the fixture creates throw-away databases named `lm_test_<run id>_<name>` and drops them afterwards (failures to drop are logged to `%TEMP%\pg-cleanup.log`). It never touches your real data. Choose one source of a server:

**A. An existing PostgreSQL server** (what the project is developed with). The role must be allowed to create and drop databases:

```powershell
$env:INFOID_TEST_POSTGRES = "Host=localhost;Port=5432;Username=postgres;Password=<password>"
dotnet test InfoID.LicenseManager/InfoID.LicenseManager.Tests
```

**B. Docker** - if `INFOID_TEST_POSTGRES` is not set and a Docker engine is running, Testcontainers starts a `postgres` container for the run.

If neither is available the PostgreSQL tests are skipped with the reason.

Cleaning up after an aborted run (databases whose name starts with `lm_test_`):

```sql
SELECT datname FROM pg_database WHERE datname LIKE 'lm_test_%';
-- DROP DATABASE "<name>";
```

## Writing tests

* Services: build a `ServiceHarness(role)` (`Fakes/ServiceHarness.cs`); it wires the real service classes to in-memory repositories, a fake clock, fake e-mail and an audit sink.
* Pages: `new WebAppFactory()`, seed `app.Admins`, sign in with `SignInAsync`, post forms with `PostFormAsync` (it adds the anti-forgery token). `Settings` and `RemoteAddress` simulate configuration and the caller's address.
* For every new action that takes an id or a posted form add: a role test (403), a "wrong HTTP method / missing token" test, and an over-posting test (extra fields and a form `id` change nothing).
* Never assert on secrets being present in output; the log-capture tests assert they are absent.

## Quality gate before merging

```bash
dotnet build InfoID.slnx                                   # 0 errors; new projects 0 warnings
dotnet test InfoID.LicenseManager/InfoID.LicenseManager.Tests   # with INFOID_TEST_POSTGRES set: 0 failed, 0 skipped
```
