# Attendance System

Employee Attendance **Clock-In / Clock-Out** system built with Clean Architecture.

The authoritative clock time **always** comes from an external Europe/Zurich time API — never
from the server or browser clock. If that time source is unavailable, clock operations fail
loudly (HTTP 503) rather than silently recording an unverified time.

## Architecture (4 layers)

```
AttendanceSystem/
├─ AttendanceSystem.Domain          # Entities, value objects, domain exceptions (zero dependencies)
├─ AttendanceSystem.Application     # CQRS commands/queries (MediatR), interfaces, behaviors, validators
├─ AttendanceSystem.Infrastructure  # EF Core, repositories, Unit of Work, time service, background jobs
├─ AttendanceSystem.Api             # Controllers, JWT, middleware, rate limiting, health checks, DI root
├─ AttendanceSystem.UnitTests       # xUnit + Moq + FluentAssertions
├─ AttendanceSystem.IntegrationTests# WebApplicationFactory end-to-end (SQL Server / Testcontainers)
├─ attendance-frontend              # React + Vite + TypeScript
└─ docker-compose.yml               # SQL Server 2022
```

**Dependency rule:** `Api → Application, Infrastructure`; `Infrastructure → Application`;
`Application → Domain`. The Domain layer references nothing but the framework.

| Layer       | Technology                                                       |
|-------------|------------------------------------------------------------------|
| Backend     | ASP.NET Core 8 Web API (targets `net8.0`)                        |
| Frontend    | React (Vite + TypeScript), TanStack Query, Zustand               |
| Database    | Microsoft SQL Server 2022 (Docker) / LocalDB for local dev       |
| ORM         | Entity Framework Core 8                                          |
| Patterns    | Clean Architecture, CQRS (MediatR), FluentValidation             |
| Resilience  | Polly (retry → circuit breaker → timeout) on the time API client |
| Logging     | Serilog (Console + rolling File)                                 |
| Auth        | JWT Bearer (15-min access token + rotating 7-day refresh token)  |
| Testing     | xUnit, Moq, FluentAssertions, Testcontainers / LocalDB           |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/) (Node 22 verified)
- For the database, **either**:
  - [Docker](https://www.docker.com/) (runs SQL Server 2022 via `docker-compose`), **or**
  - SQL Server **LocalDB** (ships with Visual Studio / SQL Server Express) for a Docker-free local run.

## Setup

### Option A — Docker (matches production config)

```bash
docker-compose up -d          # SQL Server 2022 on localhost,1433
docker ps                     # confirm attendance_sqlserver is healthy
dotnet run --project AttendanceSystem.Api
```

Uses the connection string in `appsettings.json` (`Server=localhost,1433; … User Id=sa …`).

### Option B — LocalDB (no Docker)

`appsettings.Development.json` already points `DefaultConnection` at
`(localdb)\MSSQLLocalDB`, so in the Development environment you can simply:

```bash
dotnet run --project AttendanceSystem.Api      # migrates + seeds LocalDB automatically
```

> The API applies pending EF Core migrations and seeds 3 users on startup. Seed accounts
> (password `Test@1234`): `anna@company.ch` (Employee), `hans@company.ch` (Manager),
> `admin@company.ch` (Admin).

### Frontend

```bash
cd attendance-frontend
npm install
npm run dev                   # Vite dev server on http://localhost:5173 (CORS-allowed)
```

Set `VITE_API_URL` in `attendance-frontend/.env` to point at the API (default `https://localhost:5001`).

## API Endpoints

| Method | Route                              | Auth            | Description                                        |
|--------|------------------------------------|-----------------|----------------------------------------------------|
| POST   | `/api/auth/login`                  | anonymous       | Email/password → access + refresh tokens           |
| POST   | `/api/auth/refresh`                | anonymous       | Rotate refresh token → new token pair              |
| POST   | `/api/auth/logout`                 | authenticated   | Clears the stored refresh token                    |
| POST   | `/api/attendance/clock-in`         | authenticated†  | Clock in (official Zurich time); 409 if open       |
| POST   | `/api/attendance/clock-out`        | authenticated†  | Clock out; 400 if not clocked in                   |
| GET    | `/api/attendance/status`           | authenticated   | Current `{ isClockedIn, clockInTime, duration }`   |
| GET    | `/api/attendance/history`          | authenticated   | Paged history; `?employeeId=` for Managers/Admins  |
| GET    | `/api/attendance/active`           | Manager/Admin   | All currently open sessions                        |
| PUT    | `/api/attendance/{logId}/correct`  | Admin           | Manual correction with full audit trail            |
| GET    | `/health`, `/health/live`, `/health/ready` | anonymous | Health checks (JSON)                          |

† `clock-in` / `clock-out` are rate-limited to **5 requests/minute per user** (429 beyond that).

## Environment Variables / Configuration

| Key                                   | Purpose                                                        |
|---------------------------------------|----------------------------------------------------------------|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string                                   |
| `Jwt:Secret`                          | 32+ char HMAC signing key (set via user-secrets in prod)       |
| `Jwt:Issuer` / `Jwt:Audience`         | Token issuer / audience                                        |
| `Jwt:AccessTokenExpiryMinutes`        | Access token lifetime (default 15)                             |
| `Jwt:RefreshTokenExpiryDays`          | Refresh token lifetime (default 7)                             |
| `TimeApi:BaseUrl` / `Timezone`        | External time API base + timezone (`Europe/Zurich`)            |
| `TimeApi:TimeoutSeconds` / `RetryCount` / `CircuitBreakerThreshold` / `CircuitBreakerDurationSeconds` / `CacheTtlSeconds` | Polly + cache tuning |
| `AttendanceRules:AutoTimeoutHours`    | Auto-close sessions open longer than this (default 16)         |
| `UseTimeMock` / `MockTime`            | Dev/test: use a deterministic mock time source                 |
| `VITE_API_URL` (frontend)             | API base URL for the React app                                 |

> In production set the JWT secret out of source control:
> `dotnet user-secrets set "Jwt:Secret" "<32+ char secret>" --project AttendanceSystem.Api`

## Design Decisions

- **All times stored as UTC `DATETIMEOFFSET(7)`.** Arithmetic (durations, auto-timeout) is then
  DST-safe; "midnight crossing" and Zurich wall-clock display are computed at the edges via the
  `Europe/Zurich` zone. A short 22:45→06:30 shift is flagged `crossesMidnight`.
- **External time API is the only clock source.** Attendance is a compliance record; trusting the
  server/browser clock would let it drift or be tampered with. `DateTime.UtcNow` is used only for
  non-attendance concerns (token lifetimes, audit `ChangedAt`, the cosmetic live UI clock).
- **No silent fallback.** If the time API is down, clock-in/out returns **503** (with `Retry-After`)
  — it never substitutes server time.
- **Circuit breaker + retry + timeout (Polly).** The time API is a hard dependency on the hot path;
  the breaker fails fast during an outage instead of piling up slow calls, and a 5s cache keeps
  bursts cheap without changing the source of truth.
- **One active session per employee** is enforced both in the handler (idempotency check) and by a
  unique filtered index `UX_OneActiveSession (EmployeeId WHERE ClockOutUtc IS NULL)` as a
  race-condition backstop; the concurrent loser surfaces as HTTP 409.
- **Refresh tokens are stored hashed and rotated** on every refresh (old token invalidated).
- **Tokens live only in memory on the client** (Zustand, not localStorage) to limit XSS exposure.
- **Soft delete via `IsActive`** + global query filter; the auto-timeout job intentionally ignores
  `IsActive` so a deactivated employee's open session is still closed.

## Background Jobs

- **`OpenSessionTimeoutJob`** (hosted service) runs hourly and auto-closes any session open longer
  than `AttendanceRules:AutoTimeoutHours`, setting `IsAutoTimeout = true` and writing an audit entry.

## Build & Test

```bash
dotnet build                                   # whole solution
dotnet test                                    # unit + integration tests
cd attendance-frontend && npm run build        # frontend production build
```

Integration tests use `WebApplicationFactory<Program>` against a real SQL Server. They run
against **LocalDB** by default and skip automatically if no SQL Server is reachable; set
`ATTENDANCE_TEST_CONNECTION` to point them at a Testcontainers MsSql instance in CI.

## Known Limitations & Future Improvements

- **Admin "force clock-out"** is wired in the UI but has no backend endpoint yet (clock-out derives
  the employee from the caller's JWT). Adding an admin command + endpoint is the next step.
- **Per-punch notes** are accepted/validated but not persisted (the domain has no notes field on a
  punch, only `CorrectionNotes`).
- **Admin dashboard cards** show only what the API exposes (clocked-in count, avg active duration);
  total-employee and historical-average metrics need dedicated endpoints.
- **Token expiry vs. clock skew:** `ClockSkew = 0` means tokens expire exactly at `exp`; clients
  should refresh proactively.
- Running on the .NET 9 SDK requires the .NET 8 runtime (or roll-forward) since the API targets `net8.0`.
