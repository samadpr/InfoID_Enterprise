# InfoID License Manager — Web Migration Tracker

Source of truth for the migration of the Avalonia **InfoID.AdminLicenseManager** (+ **InfoID.LicenseApi**)
to an ASP.NET Core MVC application. Statuses: `Not Started` · `In Progress` · `Implemented` · `Tested` · `Blocked`.
A feature is only `Tested` after its tests/verification steps have actually been run (evidence in the last column).

_Last updated: 2026-10-21 - Phases 1-6 done. 500/500 tests pass against a local PostgreSQL 18 (0 skipped); 477 pass + 23 skipped without a database server. Legacy projects are still in the solution (see "Open decisions")._

## Current solution (inspected)

| Project | Role | Target | Disposition |
|---|---|---|---|
| InfoID.Licensing.Core | Licensing protocol: ActivationCode, SoftCode, SerialKey (HMAC), SignedLicense (ECDSA P-256), MachineFingerprint, enums | net10.0, BCL only | **Reuse unchanged** (also referenced by InfoID.Desktop client) |
| InfoID.Licensing.Server | Entities, `LicenseDbContext` (PostgreSQL), 4 migrations, 13 services | net10.0, EF 10.0.11, Npgsql EF 10.0.0 | **Source of truth for behaviour.** Logic moves into the new layers; project kept untouched until parity is verified |
| InfoID.AdminLicenseManager | Avalonia desktop admin UI | net10.0, Avalonia 12.1.1 | **Preserved** (never deleted) |
| InfoID.LicenseApi | Minimal API used by InfoID.Desktop: activate / check-in / deactivate / phone page / accounts / trial / plans / requests | net10.0 Web | **Preserved**; endpoints re-hosted in the MVC app with identical contract |
| InfoID.Shared | Result, Paging, file logger, `Contracts/*` DTOs shared with the desktop client | net10.0 | **Reuse** (contracts must not change) |
| InfoID.Domain / Application / Infrastructure / Desktop | The ID-card designer (SQLite). Unrelated domain | net10.0 | **Not touched** — not compatible with the licensing domain |

## Feature migration map

Legend — Orig: current implementation. Target: new web implementation.

### Admin portal (desktop UI → MVC)

| # | Feature | Orig | Target | Status | Evidence |
|---|---|---|---|---|---|
| F1 | Staff sign-in, lockout (5 fails/15 min), first-run Super Admin, sign-out | AuthService, LoginViewModel | AccountController (cookie auth), IAuthService, IAdminUserRepository | Tested | Service + web flow tested with in-memory repositories (lockout, generic errors, timing equalisation, antiforgery, open-redirect, session revalidation, rate limit, first-run race). AdminUserRepository, advisory lock and the 8-way first-run race run against PostgreSQL 18 (full suite 500/500, 0 skipped) |
| F2 | Role authorization (SuperAdmin / LicenseManager / SupportAgent) | AdminContext (singleton, per-process) | Policies + `ICurrentAdmin` (per request) | Tested | Cookie auth + fallback policy (everything requires sign-in) + ManageLicenses / SuperAdmin policies tested per role. Role enforcement is also tested on every later page (403 for SupportAgent on all mutating actions, including requests, customers and plans) |
| F3 | Dashboard (counts, expiring 30/60/90, activations 30d) | DashboardService | DashboardController, IDashboardService | Tested | DashboardService + page tested (counts, UTC windows). Count SQL run against PostgreSQL 18 (full suite 500/500, 0 skipped) |
| F4a | Create activation codes (batch ≤1000, customer find-or-create) | LicenseCodeService.CreateAsync | LicensesController.Create, ILicenseService | Tested | Service + pages tested in memory: role enforcement (403), validation, batch of 1000 in one save, unique codes, customer find-or-create, audit without codes, over-posting ignored (privileged fields). Batch/uniqueness run against PostgreSQL 18 (full suite 500/500, 0 skipped) |
| F4b | Search/filter codes (status, text, serial, fingerprint) | LicenseCodeService.SearchAsync | LicensesController.Index | Tested | Criteria building, status filter, batch filter, HTML encoding tested. SQL (ILIKE with literal %/_ escaping, projection) run against PostgreSQL 18 (full suite 500/500, 0 skipped) |
| F4c | Revoke / Extend / Free machine slots / Toggle offline | LicenseCodeService | LicensesController actions (POST + antiforgery) | Tested | Each action tested: reason required, rules (perpetual, past date, already revoked) return messages, role 403, POST-only, id taken from the route only (IDOR/over-post test found and fixed a real bug), audit entries. Row lock + transaction run against PostgreSQL 18 (full suite 500/500, 0 skipped) |
| F5 | Generate serial key from request code (decode preview, tier/expiry override) | ActivationService.PreviewAsync / IssueAsync | ActivationsController, IActivationService | Tested | Issued keys validated with `SerialKey.Validate` exactly as InfoID.Desktop does (right machine/code/terms; wrong machine or code rejected; secret rotation; byte-identical to `SerialKey.Generate`). Slot/expiry/revoked/blacklist rules, overrides (manager only), no key or secret in audit/logs tested. Atomic slot limit: 12-way and 25-way races run against PostgreSQL 18 (full suite 500/500, 0 skipped), exactly the allowed number of machines activate |
| F6 | License requests: list, mark in progress, approve (creates code + email), reject | LicenseRequestService | LicenseRequestsController, ILicenseRequestService | Tested | Service tests (role enforcement, filters, approve/reject/in-progress incl. e-mail failure and rollback, throttle, audit rows), page tests (inbox, 403 for support agents, POST-only + anti-forgery, route-id-only binding, over-posting, encoding, flash messages) and PostgreSQL tests (approval race creates exactly one code, search/filters, default plans) all pass |
| F7 | Customers: search/filter, leads, details, resend verification, disable/enable | CustomerAdminService | CustomersController, ICustomerAdminService | Tested | Service tests (role enforcement, filters, approve/reject/in-progress incl. e-mail failure and rollback, throttle, audit rows), page tests (inbox, 403 for support agents, POST-only + anti-forgery, route-id-only binding, over-posting, encoding, flash messages) and PostgreSQL tests (approval race creates exactly one code, search/filters, default plans) all pass |
| F8 | Plans: list, create/edit, default seeding | PlanService | PlansController, IPlanService | Tested | Service tests (role enforcement, filters, approve/reject/in-progress incl. e-mail failure and rollback, throttle, audit rows), page tests (inbox, 403 for support agents, POST-only + anti-forgery, route-id-only binding, over-posting, encoding, flash messages) and PostgreSQL tests (approval race creates exactly one code, search/filters, default plans) all pass |
| F9 | Audit log view + hash-chain verification | AuditService | AuditController, IAuditService (chain format unchanged) | Tested | Hash function proven byte-identical to the legacy server (reflection test); chain append/verify/tamper/deleted-row and the page (HTML-encoded, POST-only verify) tested. Advisory-lock append (25 parallel writers) and legacy-chain interop run against PostgreSQL 18 (full suite 500/500, 0 skipped) |
| F10 | Activation simulator (Super Admin test tool) | ActivationSimulatorViewModel | Decide: port or drop (see open questions) | Tested | Ported as /Simulator (Super Admin only, 403 otherwise, nav link hidden). Round trip request code -> issued key -> verify tested; wrong machine rejected; no database writes |

### Client-facing API (InfoID.Desktop ↔ LicenseApi) — contract frozen

| # | Feature | Orig | Target | Status | Evidence |
|---|---|---|---|---|---|
| A1 | POST /api/v1/activate (online activation, signed token) | ActivationService.ActivateOnlineAsync | ApiControllers → IActivationService | Tested | Service tests + HTTP contract tests (exact JSON property names, status codes, no cookies/CSRF) + real PostgreSQL tests (see Phase 5 notes) |
| A2 | POST /api/v1/checkin | ActivationService.CheckInAsync | same | Tested | Service tests + HTTP contract tests (exact JSON property names, status codes, no cookies/CSRF) + real PostgreSQL tests (see Phase 5 notes) |
| A3 | POST /api/v1/deactivate | ActivationService.DeactivateOnlineAsync | same | Tested | Service tests + HTTP contract tests (exact JSON property names, status codes, no cookies/CSRF) + real PostgreSQL tests (see Phase 5 notes) |
| A4 | GET /a?c= phone/QR offline activation page | PhonePage, IssueForPhoneAsync | PhoneActivationController + Razor view | Tested | Service tests + HTTP contract tests (exact JSON property names, status codes, no cookies/CSRF) + real PostgreSQL tests (see Phase 5 notes) |
| A5 | Account: signup, verify, resend, login, forgot, reset, logout, me, licenses, requests | AccountEndpoints, CustomerAccountService | AccountApiController, ICustomerAccountService | Tested | Service tests + HTTP contract tests (exact JSON property names, status codes, no cookies/CSRF) + real PostgreSQL tests (see Phase 5 notes) |
| A6 | Trial printing: status, reserve, commit, release (atomic conditional UPDATE) | TrialService | TrialApiController, ITrialService | Tested | Service tests + HTTP contract tests (exact JSON property names, status codes, no cookies/CSRF) + real PostgreSQL tests (see Phase 5 notes) |
| A7 | GET /api/v1/plans, POST /api/v1/requests | AccountEndpoints | PlansApiController | Tested | Service tests + HTTP contract tests (exact JSON property names, status codes, no cookies/CSRF) + real PostgreSQL tests (see Phase 5 notes) |
| A8 | /health, rate-limit policies (api/account/trial/phone), request logging, 500 envelope | LicenseApi/Program.cs | Middleware + rate limiter in Web | Tested | Service tests + HTTP contract tests (exact JSON property names, status codes, no cookies/CSRF) + real PostgreSQL tests (see Phase 5 notes) |

### Infrastructure

| # | Item | Orig | Target | Status | Evidence |
|---|---|---|---|---|---|
| I1 | PostgreSQL model + 4 existing migrations (history preserved) | LicenseDbContext | LicenseManagerDbContext, IEntityTypeConfiguration per entity (18 entities, 5 files), migrations copied with original ids | Tested | Verified by tests: model == snapshot (no pending changes); migration SQL byte-identical to legacy; 4 migration ids preserved. Migrations applied to a real PostgreSQL 18 produce the legacy schema (SchemaCompatibilityTests pass) |
| I2 | Serial-key secrets (env `INFOID_LICENSE_SECRET_V1..15`) | LicenseSecretStore | ILicenseSecretProvider | Tested | ConfigurationLicenseSecretProviderTests (versions, rotation, invalid values never echoed) |
| I3 | ECDSA signing key (env PEM / file) | LicenseSigningKey | ILicenseTokenSigner | Implemented | Used by every token test via a fake with a real ECDSA key; the configuration reader itself is only exercised by running the app (the License API started with the same key file) |
| I4 | Email (SMTP / dev outbox) | EmailSender | IEmailSender (Infrastructure) | Implemented | SMTP/outbox senders written; not exercised by automated tests (e-mail is faked) |
| I5 | File logging, structured | InfoID.Shared.Logging | Serilog-free: reuse Shared provider or ILogger | Not Started | Deliberately not done: the app logs through ILogger (console / host logging). Add a file provider only if operations ask for one |
| I6 | Setup helpers (`--new-signing-key`, `--new-secret`) | Program.cs of both apps | CLI switch in Web `Program.cs` | Implemented | `--new-signing-key` / `--new-secret` in Program.cs; not executed this phase |

## Findings from inspection (to address during migration)

1. **Secrets committed to git**: DB password in `InfoID.AdminLicenseManager/appsettings.json` and `InfoID.LicenseApi/appsettings.json`. New app reads `INFOID_LICENSE_DB` / user-secrets only. The committed password should be rotated.
2. **PII committed**: `InfoID.LicenseApi/mail-outbox/*.txt` (customer emails, verification codes). Recommend `.gitignore` + removal from history.
3. **Race on activation slots**: slot check and insert are separate (check-then-insert) in `ActivationService`; concurrent requests can exceed `MaxActivations`. Will be made atomic (row lock / xmin concurrency token).
4. **`AdminContext` is a process-wide singleton** in the desktop app → unusable for a multi-user web app; replaced by per-request identity.
5. **TOTP 2FA (FR-ADM-2)** has columns but is not implemented anywhere → documented as existing gap, not a regression.
6. **Dead/placeholder files**: `Licensing.Core/Class1.cs`, `Licensing.Server/Class1.cs`; `InternalsVisibleTo InfoID.Licensing.Core.Tests` points at a project that does not exist.
7. **Serial key is a shared-secret HMAC** (documented limitation in `SerialKey.cs`). Format and algorithm are preserved; not replaced.
8. Audit hash chain uses a PostgreSQL advisory lock (`734000101`) — must be preserved so existing chain still verifies.

## Phase log

| Phase | Scope | Status |
|---|---|---|
| 0 | Inspection, tracker, proposal | Done — awaiting approval |
| 1 | Create 5 projects + references, DbContext/configurations mapped to existing schema, migration history parity, DI, build green | Done (Implemented). Solution builds 0 errors; new projects 0 warnings. Tests: 7 passed (architecture x3, model parity, migration SQL parity, migration ids, composition root). 2 schema tests blocked until Docker Desktop is running |
| 2 | F1/F2 auth + F3 dashboard + audit (F9) | Done (Implemented). Solution builds with 0 errors; new projects 0 warnings. 127 tests pass, 0 fail, 6 skipped (Docker). UI not yet viewed in a browser against a live database |
| 3 | F4 license codes, F5 serial keys, F10 simulator | Done (Implemented). Solution builds 0 errors; new projects 0 warnings. 267 tests pass, 0 fail, 13 skipped (Docker). Pages not yet viewed in a browser against a live database |
| 4 | F6 requests, F7 customers, F8 plans | Done (Tested). Service tests, page tests and PostgreSQL tests all pass (see Phase 4 notes) |
| 5 | A1–A8 client API + phone page | Done (Tested). 416 tests pass with a PostgreSQL server (INFOID_TEST_POSTGRES), 393 pass + 23 skipped without one. Solution builds with 0 errors |
| 6 | Security pass, docs (architecture, DB, migrations, testing, deployment) | Done (Tested). 500/500 tests pass with PostgreSQL (0 skipped), 477 pass + 23 skipped without. Solution builds 0 errors, 0 warnings. See Phase 6 notes |

## Phase 1 notes

- New projects live in `InfoID.LicenseManager/` (solution folder `/src/LicenseManager/`). References: Web -> Application + Infrastructure; Infrastructure -> Application + Domain; Application -> Domain; Tests -> all four plus the legacy `Licensing.Server` (only for the schema-compatibility test, remove with the legacy projects).
- `Domain` currently holds the 26 entity/constant types moved verbatim from `Licensing.Server/Entities` (same property names, so the schema is unchanged). `SupportTicket`, `FraudAlert`, `EmailTemplate` have tables but no feature uses them yet; they stay mapped so a migration never drops them. Business rules are moved into the domain/application layers feature by feature, not in bulk.
- `Application` contains only `ICurrentUser` so far; services, repositories and DTOs arrive with the feature that needs them (no empty placeholders).
- Web has the composition root only (`Program.cs`, `HttpContextCurrentUser`); no controllers or views exist yet, by design.
- Connection string: `INFOID_LICENSE_DB`, or `ConnectionStrings:LicenseDb`; `appsettings.json` has no password. Use `dotnet user-secrets set "ConnectionStrings:LicenseDb" "<...>" --project InfoID.LicenseManager/InfoID.LicenseManager.Web`.
- Run tests: `dotnet test InfoID.LicenseManager/InfoID.LicenseManager.Tests` (schema tests need Docker Desktop running; `--filter "FullyQualifiedName!~SchemaCompatibility"` skips them).

## PostgreSQL-dependent tests (no longer blocked)

These tests need a PostgreSQL server. They were blocked in Phases 1-3 (Docker Desktop not running); since Phase 4 they run against a local server via `INFOID_TEST_POSTGRES` and **all pass** (500/500 in the Phase 6 run, 0 skipped). Without a server or Docker they are reported as **Skipped**, never as passed. See `InfoID.LicenseManager/docs/TESTING.md`.

| Test | Proves |
|---|---|
| SchemaCompatibilityTests (2) | Migrations applied to a real database produce the same schema as the legacy server; a legacy-migrated database needs no further migration |
| PostgresRepositoryTests.Concurrent_audit_writers... | 25 parallel audit writers still produce one unbroken hash chain (advisory lock) |
| PostgresRepositoryTests.A_chain_written_by_the_legacy_server... | Chains written by the legacy `AuditService` verify in the new one and vice versa |
| PostgresRepositoryTests.First_run_setup_is_atomic... | 8 racing first-run requests create exactly one administrator |
| PostgresRepositoryTests.Dashboard_queries... | Dashboard SQL counts only live rows in the right windows |
| PostgresLicenseWorkflowTests.Parallel_requests... (3) | 12 parallel requests for the last slot let exactly 1 through; 25 for a 5-machine code activate exactly 5; 10 duplicate requests from one machine leave 1 active record |
| PostgresLicenseWorkflowTests.A_revoke_racing... | A revoke racing with activations leaves no active machine on a revoked code |
| PostgresLicenseWorkflowTests.A_batch_is_stored... | 300 codes unique, one customer, one audit entry in a real database |
| PostgresLicenseWorkflowTests.Search_finds... | ILIKE search by code/customer/machine; typed %% and _ are literal |
| PostgresLicenseWorkflowTests.Freeing_slots... | Transfer history stored, code reopens |

## Phase 2 notes

- **Auth**: ASP.NET Core cookie authentication (HttpOnly, SameSite=Strict, Secure outside development, 2 h sliding). `AdminCookieEvents` re-validates the account on every request, so a disabled/locked account or a changed role ends the session at once. Every endpoint requires sign-in unless marked `[AllowAnonymous]` (login, setup, error only). CSRF: `AutoValidateAntiforgeryToken` on every unsafe verb. Login/setup are rate limited (10/min per address) on top of the per-account lockout (5 failures, 15 min).
- **Behaviour kept from the desktop manager**: PBKDF2 hashes (format identical, verified both directions), password policy, lockout rule, audit action names and hash chain, advisory-lock keys.
- **Deliberate differences**: unknown e-mail now spends the same PBKDF2 time as a wrong password (no user enumeration by timing); lockout message gives minutes remaining (server time zone is not the user's); password capped at 128 characters on creation; first-run setup is atomic and closes once any administrator exists; Audit verify is POST-only.
- **Bug found by tests**: error pages are re-executed with the failed request's method, so a failing POST was turned into a CSRF 400 by the global antiforgery filter. `ErrorController` is now exempt (regression tests added).
- **Known limits**: failed-login counter is read-modify-write (two simultaneous wrong guesses may count once; the rate limiter bounds this). Behind a reverse proxy the rate limiter sees the proxy address until forwarded headers are configured (deployment guide, Phase 6). TOTP 2FA still not implemented (was not in the desktop manager either).
- Migrations are not applied automatically; set `Database:MigrateOnStartup=true` to opt in.

## Phase 3 notes

- **Layering of the workflow**: `LicensesController` / `ActivationsController` / `SimulatorController` -> `ILicenseService` / `IActivationService` / `IActivationSimulatorService` -> repositories (`ILicenseRepository`, `ICustomerRecordRepository`, `IBlacklistRepository`, `IActivationRepository`) + `IUnitOfWork`. Licensing rules live in the Domain (`LicenseCode.Create/Revoke/Extend/FreeMachineSlots/RegisterActivation`, `ActivationRules`); key/code formats stay in `InfoID.Licensing.Core` and are called unchanged.
- **Error convention** (now used everywhere new): a failed `Result` = something staff can fix in the form (validation, a rule); `ForbiddenException` = role may not do this (HTTP 403, logged as a warning); `NotFoundException` = unknown/stale id (HTTP 404). `DomainRuleException` never escapes a service.
- **Atomic activation limit (the race noted in Phase 0)**: every mutation of a code (issue key, revoke, extend, free slots, toggle offline) runs in one transaction that first takes `SELECT ... FOR UPDATE` on the code's row, then re-evaluates the rules on fresh data. Two requests for the last slot queue; the second sees the slot taken. Re-activating the same machine replaces its record. Audit entries are written after the commit, in their own transaction (same as the desktop manager).
- **Bugs found by the tests and fixed**: (1) a read-only helper property on `LicenseCode` was mapped by EF as a second relationship (would have created a `LicenseCodeId1` column and FK on `ActivationRecord`); caught by the model-vs-snapshot test, fixed with `Ignore`. (2) Route ids could be overridden by a form field named `id` (form values bind before route values), allowing a request for license A to act on license B; all id parameters are now `[FromRoute]`.
- **Deliberate differences from the desktop manager**: revoking an already-revoked code is refused (keeps the original revoke date); a failed rule on an action shows a message instead of an exception; search treats typed `%`/`_` literally; access denied is a 403 page for every path; codes in lists are shown as XXXX-XXXX-XXXX-XXXX; a Details page per code (not in the desktop UI) hosts the actions, so every action names its code in the URL.
- **Secrets**: `ConfigurationLicenseSecretProvider` reads `INFOID_LICENSE_SECRET_V1..V15` (environment variables as before, or user-secrets in development). Invalid values stop the app at first use with a message naming the setting, never the value. Never logged or audited.
- **Not done / known limits**: creating a code does not yet send e-mail; Trial licenses are not offered in the form (same as the desktop manager); phone/QR and online activation (A1-A4) use this same service in Phase 5.

## Phase 4 notes

Written and building: `LicenseRequest/Plan/CustomerAccount` domain rules, `PlanDetails`, `LicenseCodeFactory` (shared by staff creation and approval), `CustomerMailer`, `VerificationCodeService`, `PlanService`, `LicenseRequestService` (approve = one transaction under a row lock on the request), `CustomerAdminService`, 4 new repositories, SMTP/outbox e-mail senders, `RequestsController`/`CustomersController`/`PlansController` with views and nav links.
Done afterwards: service tests for requests, customers and plans (`LicenseRequestServiceTests`, `CustomerAdminServiceTests`, `PlanServiceTests`), page tests (`RequestCustomerPlanPagesTests`: 403s, POST-only/anti-forgery, route-id-only binding, over-posting, encoding), PostgreSQL tests (parallel approval, SQL filters, default plans), `Database:MigrateOnStartup` also seeds the default plans, `mail-outbox/` is git-ignored.
Found by tests: `:N0` number formatting is culture-dependent (a plan message printed "1,02,300"); now invariant, in the new code and in the card-limit message of `InfoID.Licensing.Core`. When the expiry date is wrong and another terms error exists, only the terms error is reported (the expiry check is skipped); the test asserts this.

## Phase 5 notes (client API, hosted in the web app)

- **Same app, same services.** `/api/v1/*`, `/a` and `/health` live in `InfoID.LicenseManager.Web` (`Api/` controllers + `PhoneActivationController`) and call `IOnlineActivationService`, `IActivationService.IssueForPhoneAsync`, `ICustomerAccountService`, `ITrialService`, `IPlanService`, `ILicenseRequestService`. No separate API project and no duplicated licensing logic. Routes, JSON property names, status codes (401 for a bad token, 200 + `success:false` for refusals, 429, JSON 500 with trace id) and rate-limit numbers (20/15/60/10 a minute per address) are those of the original License API; the contracts are the shared `InfoID.Shared.Contracts` classes.
- **Stateless**: the API is anonymous and bearer-token based, so it is exempt from the CSRF check and sets no cookies (tested). Staff pages keep CSRF, cookies and role policies.
- **Verified on a real PostgreSQL 18 (local server, throwaway `lm_test_*` databases that are dropped afterwards)**: 12 parallel activations for the last slot let exactly 1 through; 25 for a 5-machine code activate exactly 5; 40 parallel trial reservations never exceed the limit of 10 and create one usage row; two staff approving one request create exactly one license; migrations equal the legacy server's schema; audit chains written by the legacy server verify here and vice versa. Run them with `INFOID_TEST_POSTGRES=<admin connection string>` (or Docker Desktop running); without either they are reported as skipped.
- **Bugs found by tests this phase**: the exception handlers of the staff site sat inside the API error middleware, so an API failure returned the HTML/developer error page instead of the JSON 500 (fixed: handlers skip `/api` and `/a`); a `ConnectionIdleLifetime` setting in the test fixture was invalid; PostgreSQL ran out of client slots because pooled connections of earlier test databases piled up (pools are cleared per test).
- **Deliberate differences**: lockout messages give minutes instead of a clock time; customer passwords are capped at 128 characters; a signing key that is missing no longer stops the web app (the staff site keeps working) - activation answers "The license server is not configured (no signing key)." and a warning is logged at start; `/health` is part of the web app.
- **Not done**: file logging provider of `InfoID.Shared` (the app logs through ILogger/console); the desktop's configured base URL must be pointed at this web app (see deployment notes in Phase 6); old LicenseApi/AdminLicenseManager/Licensing.Server are still in the solution, unchanged.

## Phase 6 notes (security pass, documentation)

- **Security pass** (details and checklist in `InfoID.LicenseManager/docs/SECURITY.md`): added `Permissions-Policy`, COOP/CORP headers; Kestrel server header off and 1 MB body limit; opt-in forwarded-header support restricted to `Proxy:KnownProxies` (so the rate limiter and audit log see the real caller behind a proxy, and a client cannot spoof its address); `DataProtection:KeysPath` for persistent cookie/anti-forgery keys; production start-up warnings for a missing key path or `AllowedHosts: *`. New `HardeningTests` prove the headers and the proxy behaviour (spoofed `X-Forwarded-For` is ignored unless the sender is a configured proxy).
- **Documentation**: `InfoID.LicenseManager/README.md` and `docs/` (ARCHITECTURE, DATABASE, MIGRATIONS, TESTING, DEPLOYMENT, SECURITY).
- **Not verified by automated tests**: SMTP delivery to a real mail server; the ECDSA key/secret readers with real files on a production-like host (exercised by hand when the app was started); browser rendering of every page (HTML is asserted, not screenshots); behaviour behind a real nginx/IIS proxy (simulated with a fake remote address).

## Open decisions (need the owner)

1. **Rotate the PostgreSQL password** that is committed in `InfoID.AdminLicenseManager/appsettings.json` and `InfoID.LicenseApi/appsettings.json` (tracked by git), and use `INFOID_LICENSE_DB` / user-secrets instead.
2. **Remove the legacy projects** (`InfoID.AdminLicenseManager`, `InfoID.LicenseApi`, and later `InfoID.Licensing.Server`) after the web site has run in production for a release cycle. Nothing was deleted; all of them still build.
3. **Point InfoID.Desktop at the new site** (`LicenseServerSettings.ProductionUrl` / `INFOID_LICENSE_SERVER_URL`; the Debug default still targets the legacy API port 7043).
4. Decide whether staff need a second factor / SSO (not implemented).
