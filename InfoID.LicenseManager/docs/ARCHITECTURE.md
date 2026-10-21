# Architecture

## Layers and dependency direction

```
        Web  ──────────────►  Application  ──────────►  Domain  ──►  InfoID.Licensing.Core
         │                        ▲                        ▲
         │ (composition root)     │                        │
         └────────►  Infrastructure ───────────────────────┘
```

| Layer | Contains | May reference |
|---|---|---|
| **Domain** | Entities with behaviour (`LicenseCode`, `LicenseRequest`, `CustomerAccount`, `AdminUser`, `Plan`...), rules (`ActivationRules`, `AuditChain`), value objects (`PlanDetails`), `DomainRuleException` | `InfoID.Licensing.Core` only |
| **Application** | Services (one interface per use-case area), DTOs, validators, mappings, `ICurrentUser`, and the interfaces the outer layers implement (repositories, `IUnitOfWork`, `IPasswordHasher`, `ILicenseTokenSigner`, `IEmailSender`...) | Domain, `InfoID.Shared`, `InfoID.Licensing.Core` |
| **Infrastructure** | `LicenseManagerDbContext`, entity configurations, migrations, repository implementations, `UnitOfWork`, PBKDF2 hasher, signing key and secret readers, SMTP / file-outbox mail | Application, Domain |
| **Web** | MVC controllers, API controllers, ViewModels, views, filters, middleware, authentication and rate-limit setup | Application, Infrastructure (only in `Program.cs` to register services) |

Controllers never see entities or `DbContext`: they receive DTOs from services and map them to ViewModels (`Web/Mappings`). Views bind only to ViewModels.

## Request flow

```
HTTP ─► middleware (logging, error envelope, security headers, rate limiter, auth)
     ─► controller action (model binding into a ViewModel/DTO, ModelState check)
     ─► service (authorisation re-check via ICurrentUser, validation, orchestration)
     ─► repositories + IUnitOfWork (transaction, row locks)  ─► PostgreSQL
     ◄─ Result / Result<T> (expected failures)  or  ForbiddenException / NotFoundException (mapped to 403 / 404 by a filter)
```

* **Expected failures** (bad input, rule broken, already approved) are `Result` values with a user-readable message.
* **Authorisation** is enforced twice: policies on controller actions (`ManageLicenses`, `SuperAdmin`, plus a fallback policy that requires sign-in everywhere) and again inside the services through `ICurrentUser`, so a service called from another entry point stays protected.
* **Identifiers** are bound only from the route (`[FromRoute] long id`). A form field called `id` cannot redirect an action to another record, and sub-forms bind through `[Bind(Prefix = ...)]` so only the intended fields are accepted (no over-posting).

## Data consistency

| Concern | Mechanism |
|---|---|
| Activation slot limit (N machines per code) | The `LicenseCode` row is read with `SELECT ... FOR UPDATE` inside a transaction (`ExecuteInTransactionAsync`), so parallel activations are serialised |
| Approving a request twice | Request row locked for update; the second caller sees the decided status and gets a message. Verified against PostgreSQL: exactly one code is created |
| Trial printing limit | One conditional `UPDATE ... WHERE used + n <= limit` (no read-then-write), verified under parallel load |
| Audit log tamper evidence | Each row hashes the previous one (`AuditChain`); appends take a PostgreSQL advisory lock (734000101). The hash function is byte-identical to the legacy server so existing chains verify |
| First Super Admin | Advisory lock 734000102: two simultaneous setups cannot both succeed |

## Compatibility with the existing system (unchanged on purpose)

* Database schema and the four migrations (ids preserved, model matches the snapshot - tested).
* Activation code, soft code and serial key formats, HMAC secret versions `INFOID_LICENSE_SECRET_V1..15`, ECDSA P-256 token format `IIDL1.`.
* JSON contracts in `InfoID.Shared.Contracts`, route names, status codes and rate limits of the client API.
* Password hash format `PBKDF2$iterations$salt$hash`.

## Client API vs staff site

| | Staff site | Client API (`/api/v1/*`, `/a`, `/health`) |
|---|---|---|
| Authentication | Cookie `InfoID.Admin` (HttpOnly, SameSite=Strict, Secure outside development, 2 h sliding, re-validated against the database on every request) | None for the endpoints that bootstrap (activate, sign-up...), bearer tokens afterwards |
| CSRF | Anti-forgery token on every POST (global filter) | Not applicable: no cookies are used |
| Errors | Friendly HTML pages with a trace id, no internals | JSON; unexpected errors answer `{ success: false, ... , traceId }` with status 500 |
| Rate limit | Login/setup: 10 per minute per address | api 20, account 15, trial 60, phone 10 per minute per address |

## Notable design decisions

* **No generic repository.** Each repository interface exposes exactly the queries a use case needs (for example `FindForUpdateAsync`, `CountByStatusAsync`), which keeps locking and projections explicit.
* **`TimeProvider` everywhere** so expiry, lockout and throttle rules are testable without sleeping.
* **Views contain no inline script or style**, which lets the content-security policy forbid both.
* **Secrets never go through logs or audit rows**: serial keys, activation tokens, passwords and the signing key are excluded (tested by capturing the log output).
