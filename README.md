# Attendance System

A production-grade employee clock-in/clock-out system built with ASP.NET Core 8 and React 18.

The authoritative clock time **always** comes from an external Europe/Zurich time API — never from
the server or browser clock. If that source is unavailable, clock operations fail loudly (HTTP 503)
rather than silently recording an unverified time.

## Architecture

Clean Architecture with four layers; source dependencies point **inward** toward a
dependency-free Domain.

```
┌──────────────────────────────────────────────────────────────┐
│  Api            controllers, JWT, middleware, rate limiting    │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │  Infrastructure   EF Core, time client, Polly, jobs        │ │
│  │  ┌──────────────────────────────────────────────────────┐ │ │
│  │  │  Application    CQRS commands/queries, interfaces       │ │ │
│  │  │  ┌──────────────────────────────────────────────────┐ │ │ │
│  │  │  │  Domain   entities, value objects (no deps)        │ │ │ │
│  │  │  └──────────────────────────────────────────────────┘ │ │ │
│  │  └──────────────────────────────────────────────────────┘ │ │
│  └──────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
   Api → Application, Infrastructure   ·   Infrastructure → Application   ·   Application → Domain
```

The frontend (React + Vite + TypeScript) is a separate SPA that talks to the API over HTTP.
A full deep-dive lives in [`PROJECT_EXPLAINED.md`](./PROJECT_EXPLAINED.md).

## Tech Stack

| Layer        | Technology                              | Purpose                                              |
|--------------|-----------------------------------------|------------------------------------------------------|
| Backend      | ASP.NET Core 8 Web API (`net8.0`)       | HTTP API, hosting, DI, middleware                    |
| Frontend     | React 18 + Vite + TypeScript            | Single-page application                              |
| Database     | SQL Server 2022 (Docker) / LocalDB      | Persistence                                          |
| ORM          | Entity Framework Core 8                 | Mapping, migrations, parameterized SQL               |
| CQRS         | MediatR                                 | Command/query dispatch + pipeline behaviors          |
| Validation   | FluentValidation                        | Request validation (in a MediatR behavior)           |
| Resilience   | Polly                                   | Retry → circuit breaker → timeout on the time API    |
| Auth         | JWT Bearer + BCrypt                      | Stateless auth, hashed passwords, refresh rotation   |
| Logging      | Serilog                                 | Structured console + rolling file logs               |
| Server state | TanStack React Query                    | Caching, refetch, cache invalidation                 |
| Client state | Zustand                                 | In-memory auth tokens (XSS-resistant)                |
| Tests        | xUnit, Moq, FluentAssertions, Testcontainers | Unit + end-to-end integration                  |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/) (Node 22 verified)
- A SQL Server — **either** [Docker](https://www.docker.com/) (SQL Server 2022 via compose)
  **or** SQL Server **LocalDB** (Visual Studio / SQL Server Express) for a Docker-free run.

## Quick Start

```bash
# 1. Database (Docker)
docker-compose up -d                       # SQL Server 2022 on localhost,1433
#    …or skip Docker: Development config already points at (localdb)\MSSQLLocalDB

# 2. API  (migrates + seeds on startup; Swagger at /swagger in Development)
dotnet run --project AttendanceSystem.Api
#    If only the .NET 9 SDK is installed (no .NET 8 runtime):
#    DOTNET_ROLL_FORWARD=Major dotnet run --project AttendanceSystem.Api --no-launch-profile

# 3. Frontend  (Vite dev server, http://localhost:5173 — set VITE_API_URL in attendance-frontend/.env)
cd attendance-frontend
npm install
npm run dev
```

Seed accounts (password `Test@1234`): `anna@company.ch` (Employee), `hans@company.ch` (Manager),
`admin@company.ch` (Admin).

## API Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/auth/login` | anonymous | Email/password → access + refresh tokens |
| POST | `/api/auth/refresh` | anonymous | Rotate refresh token → new token pair |
| POST | `/api/auth/logout` | authenticated | Clear the stored refresh token |
| POST | `/api/attendance/clock-in` | authenticated† | Clock in (official Zurich time); 409 if already in |
| POST | `/api/attendance/clock-out` | authenticated† | Clock out; Manager/Admin may pass `{employeeId}` to force-clock-out another |
| GET | `/api/attendance/status` | authenticated | Caller status + current Zurich time |
| GET | `/api/attendance/history` | authenticated | Own history; Manager/Admin may pass `?employeeId=` |
| GET | `/api/attendance/active` | Manager/Admin | All currently open sessions |
| PUT | `/api/attendance/{logId}/correct` | Admin | Manual correction (audited) |
| GET | `/api/employees` | Manager/Admin | List all employees (incl. inactive) |
| POST | `/api/employees` | Admin | Create employee (409 on duplicate email/badge) |
| PUT | `/api/employees/{id}/role` | Admin | Change role |
| PUT | `/api/employees/{id}/deactivate` | Admin | Soft-delete (IsActive = false) |
| PUT | `/api/employees/{id}/activate` | Admin | Reactivate |
| GET | `/health`, `/health/live`, `/health/ready` | anonymous | Health checks (JSON) |

† `clock-in` / `clock-out` are rate-limited to **5 requests/minute per user** (429 beyond that).

## Roles & Permissions

| Feature | Employee | Manager | Admin |
|---------|:--------:|:-------:|:-----:|
| Clock in / out (self) | ✅ | ✅ | ✅ |
| View own status & history | ✅ | ✅ | ✅ |
| View another employee's history (`?employeeId=`) | ❌ (403) | ✅ | ✅ |
| View all active sessions | ❌ | ✅ | ✅ |
| Force clock-out another employee | ❌ | ✅ | ✅ |
| List all employees | ❌ | ✅ | ✅ |
| Manually correct a log | ❌ | ❌ | ✅ |
| Create employee / change role / (de)activate | ❌ | ❌ | ✅ |

The frontend `ProtectedRoute` mirrors this for navigation; the **authoritative** check is server-side.

## Design Decisions

- **External time API is the only clock source.** Attendance is a compliance record; trusting the
  server/browser clock would let it drift or be tampered with. (Currently `timeapi.io`; the client
  parses both offset-qualified and zone+local response shapes.)
- **No silent fallback.** If the time source is down, clock-in/out returns **503** with a
  `Retry-After` header — it never substitutes server time, and no record is written.
- **All timestamps stored as UTC-anchored `DATETIMEOFFSET(7)`.** Offset-bearing instants make
  duration math DST-safe; Europe/Zurich wall-clock is rendered only for display.
- **Circuit breaker + retry + timeout (Polly).** The time API is a hot-path dependency; the breaker
  fails fast during an outage instead of piling up slow calls.
- **One active session per employee**, enforced in the handler *and* by a unique filtered index
  (`UX_OneActiveSession`) as a race-condition backstop → 409.
- **JWT in memory (not localStorage).** Short-lived access tokens + rotating, hashed refresh tokens
  limit the blast radius of an XSS or a stolen token.
- **Every mutation is audited.** Clock-in/out, auto-timeout, manual corrections, and all employee
  management actions write before/after snapshots to `AuditLogs`.

## Project Structure

```
AttendanceSystem/
├─ AttendanceSystem.Domain/          # Entities, value objects, domain exceptions (zero dependencies)
├─ AttendanceSystem.Application/     # CQRS commands/queries, interfaces, behaviors, validators
├─ AttendanceSystem.Infrastructure/  # EF Core, repositories, Unit of Work, time service, background job
├─ AttendanceSystem.Api/             # Controllers, JWT, middleware, rate limiting, health checks, DI root
├─ AttendanceSystem.UnitTests/       # Fast in-memory tests (domain + handlers, mocked)
├─ AttendanceSystem.IntegrationTests/# End-to-end tests over a real SQL Server (WebApplicationFactory)
├─ attendance-frontend/              # React + Vite + TypeScript SPA
│  └─ src/
│     ├─ api/                        # Axios client + per-resource API modules
│     ├─ components/                 # ClockButton, AttendanceHistory, AdminDashboard, UserManagement, common
│     ├─ hooks/                      # useAttendance, useAuth, useCurrentTime, useNetworkStatus
│     ├─ pages/                      # Login, Dashboard, History, Admin, UserManagement
│     ├─ store/                      # Zustand stores (auth, toasts)
│     ├─ types/ utils/               # Shared types and helpers
│     └─ index.css                   # Design system (dark, Inter, sidebar layout)
├─ docker-compose.yml                # SQL Server 2022
├─ README.md
└─ PROJECT_EXPLAINED.md              # Full architectural deep-dive
```
