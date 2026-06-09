# PROJECT_EXPLAINED.md — The Attendance System Masterclass

> A senior engineer's complete walkthrough of the Employee Attendance (Clock-In / Clock-Out)
> system. This document explains **what** the code does, **how** it does it, and — most
> importantly — **why** every decision was made. Read it top to bottom and you will understand
> the entire codebase, from the database constraints up to the React components.

---

## Table of Contents

1. [Project Overview & Purpose](#section-1-project-overview--purpose)
2. [Clean Architecture — The Foundation](#section-2-clean-architecture--the-foundation)
3. [Domain Layer — Line by Line](#section-3-domain-layer--line-by-line)
4. [Application Layer — Line by Line](#section-4-application-layer--line-by-line)
5. [Infrastructure Layer — Line by Line](#section-5-infrastructure-layer--line-by-line)
6. [API Layer — Line by Line](#section-6-api-layer--line-by-line)
7. [React Frontend — Line by Line](#section-7-react-frontend--line-by-line)
8. [Full Request Flows](#section-8-full-request-flows)
9. [Database Schema Reference](#section-9-database-schema-reference)
10. [Complete File Map](#section-10-complete-file-map)
11. [Configuration Reference](#section-11-configuration-reference)
12. [Security Model](#section-12-security-model)
13. [Edge Cases & How Each Is Handled](#section-13-edge-cases--how-each-is-handled)
14. [Running the Project — Full Setup Guide](#section-14-running-the-project--full-setup-guide)

---

# SECTION 1: Project Overview & Purpose

## 1.1 What this system does

The Attendance System is a full-stack web application that lets employees **clock in** when they
start work and **clock out** when they finish. Every clock-in/clock-out is a permanent,
auditable record of when a person was working. Managers and administrators can review history,
see who is currently working, and correct mistakes.

It has three kinds of users:

| Role       | What they can do                                                                 |
|------------|----------------------------------------------------------------------------------|
| `Employee` | Clock in, clock out, see their own attendance history and current status.        |
| `Manager`  | Everything an Employee can do, plus view all active sessions and others' history.|
| `Admin`    | Everything a Manager can do, plus manually correct attendance records.           |

The system is split into two deployable pieces:

- A **backend HTTP API** (ASP.NET Core 8) that owns all business rules and the database.
- A **frontend single-page application** (React + TypeScript) that talks to the API.

## 1.2 The core business constraint: time must come from an external source

This is the single most important rule in the entire system, and almost every design decision
flows from it:

> **All attendance timestamps MUST come from an external, authoritative time source for the
> Europe/Zurich timezone. The system must NEVER use the server's clock (`DateTime.Now` /
> `DateTime.UtcNow`) or the browser's clock to record a clock-in or clock-out.**

Why is this so strict? Attendance records are a **compliance and payroll artifact**. They may be
used to:

- Calculate wages and overtime.
- Prove regulatory compliance (e.g. maximum shift length, mandatory rest periods).
- Resolve disputes between an employer and an employee.

If the recorded time came from the server clock, then anyone who could change the server's clock
(an administrator, a misconfigured NTP daemon, a virtual machine with clock drift) could silently
alter the historical record. If it came from the browser, **every employee could trivially cheat**
by changing the clock on their own laptop before clicking "Clock In." Neither is acceptable.

By forcing the time through one **external, neutral, audited source**, we get a single trusted
clock that no individual participant controls. We also record *which* source produced each
timestamp (the `Source` field), so an auditor can later verify provenance.

A crucial corollary: **if the trusted time source is unavailable, the operation must fail.** There
is no "fall back to the server clock." A clock-in with an untrustworthy time is worse than no
clock-in at all, because it silently corrupts the compliance record. When the time API is down,
the API returns HTTP `503 Service Unavailable` and the employee is asked to try again shortly.

> Note on timezone vs. storage: We *display* Europe/Zurich wall-clock time to users, but we
> **store** every timestamp as a `DateTimeOffset` (an instant in time plus its UTC offset). This
> is the best of both worlds — see Section 3 (`AttendanceLog.cs`) and Section 9 (schema) for the
> full explanation of why `DateTimeOffset` and not `DateTime`.

## 1.3 What "clock-in" and "clock-out" mean (data + rules)

**Clock-in** creates a new `AttendanceLog` row:

- `ClockInUtc` = the authoritative instant from the external time service.
- `ClockOutUtc` = `NULL` (the session is "open").
- `ClockInSource` = the URL/identifier of the time source.

**Clock-out** updates that same open row:

- `ClockOutUtc` = a fresh authoritative instant.
- `ClockOutSource` = the time source.
- The shift `Duration` becomes computable (`ClockOutUtc - ClockInUtc`).

The business rules enforced around these two operations:

1. **One open session per employee.** You cannot clock in twice without clocking out. This is
   enforced in three places (defense in depth): the command handler checks for an existing open
   session, the database has a *unique filtered index*, and the API surfaces a `409 Conflict`.
2. **Clock-out requires an open session.** Clocking out when not clocked in returns `400`.
3. **Clock-out must be after clock-in.** Enforced in the domain entity *and* by a database
   `CHECK` constraint.
4. **Forgotten sessions are auto-closed.** A background job closes any session left open longer
   than a configurable threshold (default 16 hours) and flags it `IsAutoTimeout = true`.
5. **Corrections are audited.** An Admin can change recorded times, but the change is recorded in
   an `AuditLog` with both the old and new values, and the record is flagged `IsManualCorrection`.

## 1.4 Technology choices and *why*

### Backend: ASP.NET Core 8 Web API
A mature, fast, cross-platform framework with first-class dependency injection, middleware, and
hosting primitives (background services, health checks, rate limiting built in). Targeting
`net8.0` (LTS) gives long-term support.

### Frontend: React 18 + TypeScript (Vite)
React is the dominant component model; TypeScript adds compile-time type safety that catches
whole classes of bugs before runtime. Vite gives near-instant dev server start and fast builds.

### Database: Microsoft SQL Server + Entity Framework Core 8
SQL Server has excellent support for the exact features this domain needs: `DATETIMEOFFSET(7)`
for instant-plus-offset storage, **filtered indexes** (the "one open session" rule), and
`CHECK` constraints. EF Core 8 is the ORM: it maps C# objects to tables, runs migrations, and —
critically — generates **parameterized SQL** that is immune to SQL injection.

### MediatR (CQRS dispatch) — *why not direct service calls?*
Each use case (ClockIn, ClockOut, GetHistory…) is a self-contained **Command** or **Query** with
its own **Handler**. Controllers don't call services directly; they build a request object and
`Send()` it through MediatR.

Why bother instead of injecting a `IAttendanceService` and calling a method?

- **Cross-cutting behavior for free.** MediatR runs every request through a *pipeline*. We slot in
  a `ValidationBehavior` (validate the request) and a `LoggingBehavior` (time + log it) *once*,
  and they apply to *every* command and query automatically. With direct service calls you would
  repeat validation/logging in every method.
- **Single Responsibility.** One handler does one thing. No 2,000-line "AttendanceService" god class.
- **Testability.** A handler has explicit dependencies and a single `Handle` method — trivial to
  unit test with mocks.

The trade-off (more files, a little indirection) is worth it for a system with many discrete
use cases and strict cross-cutting requirements (validation, logging, auditing).

### Polly (resilience) — *why not manual retry?*
The external time API is a network dependency on the hot path of every clock-in. Polly gives us
declarative **retry**, **circuit breaker**, and **timeout** policies composed together. Hand-rolling
a correct circuit breaker (tracking failure counts, half-open probing, thread-safety) is
surprisingly hard to get right; Polly is battle-tested. See Section 5.

### Zustand (frontend state) — *why not Redux?*
We need a tiny amount of global state (the auth tokens + current user). Redux brings boilerplate
(actions, reducers, store config, middleware) that is overkill here. Zustand is a ~1KB store with
a hook-based API and no providers required. It is perfect for "a couple of pieces of global state."

### TanStack React Query (server state) — *why not useEffect + fetch?*
Attendance status, history, and the active-employee list are **server state**: cached, refetched,
invalidated. React Query handles caching, background refetching (`refetchInterval`), staleness,
loading/error states, and cache invalidation after mutations. Doing this by hand with `useEffect`
leads to race conditions and stale data. React Query is the right tool for *server* state, while
Zustand handles *client* state (tokens) — a deliberate separation.

### BCrypt (password hashing)
Passwords are never stored in plain text. BCrypt is an adaptive hashing algorithm with a tunable
work factor, designed specifically to be slow enough to resist brute force. See Section 12.

### Serilog (logging)
Structured logging to console + rolling file, configured from `appsettings.json`.

---

# SECTION 2: Clean Architecture — The Foundation

## 2.1 What Clean Architecture is and what problem it solves

Clean Architecture (and its cousins Hexagonal/Onion architecture) is about one idea:

> **Dependencies point inward, toward the business rules. The business rules know nothing about
> the database, the web, or any external framework.**

The problem it solves is **coupling to volatile details**. Frameworks, databases, and UI
technologies change often. Business rules ("an employee can only have one open session")
change rarely. If your business logic imports EF Core, ASP.NET, or React types, then a change to
any of those forces a change to your core logic, and your core logic becomes impossible to test
without spinning up a database or a web server.

Clean Architecture inverts this. The core (Domain) is a plain C# library with **zero external
dependencies**. Everything else depends on the core; the core depends on nothing. This is the
**Dependency Rule**: source code dependencies only ever point *inward*.

The concentric circles, from innermost to outermost:

```
        ┌─────────────────────────────────────────────┐
        │                   API                        │   (frameworks, HTTP, DI wiring)
        │   ┌─────────────────────────────────────┐    │
        │   │           Infrastructure             │    │   (EF Core, HttpClient, Polly)
        │   │   ┌─────────────────────────────┐    │    │
        │   │   │        Application           │    │    │   (use cases, interfaces)
        │   │   │   ┌─────────────────────┐    │    │    │
        │   │   │   │      Domain          │    │    │    │   (entities, value objects)
        │   │   │   │  (no dependencies)   │    │    │    │
        │   │   │   └─────────────────────┘    │    │    │
        │   │   └─────────────────────────────┘    │    │
        │   └─────────────────────────────────────┘    │
        └─────────────────────────────────────────────┘
                 dependencies point INWARD ──►
```

## 2.2 Mapping every project to its layer

### `AttendanceSystem.Domain` — the core
**Contains:** entities (`Employee`, `AttendanceLog`, `AuditLog`), the value object `ZurichTime`,
domain exceptions, and the `Roles` constants.

**Cannot reference:** anything. Its `.csproj` has **zero** `PackageReference` and **zero**
`ProjectReference` entries. No EF Core, no MediatR, no ASP.NET. Only the .NET base class library.

**Why:** these are the rules and data shapes that define *what the business is*. They must be
expressible and testable without any infrastructure. If you deleted SQL Server, React, and
ASP.NET, the Domain would still compile and still be correct.

### `AttendanceSystem.Application` — the use cases
**Contains:** CQRS commands/queries and their handlers, the **interfaces** for things it needs
(`IUnitOfWork`, `IAttendanceRepository`, `ITimeService`…), the MediatR pipeline behaviors
(validation, logging), FluentValidation validators, and DTOs.

**Knows:** the Domain, and abstractions (interfaces) of infrastructure.
**Does NOT know:** *how* those interfaces are implemented. It calls `ITimeService.GetCurrentTimeAsync()`
without any idea that the implementation uses an HttpClient, Polly, or a cache.

**Why:** this is the application-specific orchestration ("to clock in: verify employee, check for
open session, fetch time, create log, audit, save"). It depends only on the Domain plus the
interfaces it *declares for itself*. This is the **Dependency Inversion Principle**: the
Application defines the contract; Infrastructure conforms to it.

### `AttendanceSystem.Infrastructure` — the details
**Contains:** the EF Core `AppDbContext`, entity configurations, repository + Unit-of-Work
implementations, the `WorldTimeApiClient`, `CachedTimeService`, Polly policies, the
`OpenSessionTimeoutJob` background service, and the database seeder.

**Why it is the "outer" layer:** it *implements* the interfaces the Application declared. It is
full of volatile detail — connection strings, HTTP, JSON parsing, retry policies. Because it
depends inward (on Application + Domain), we can swap SQL Server for PostgreSQL, or WorldTimeAPI
for timeapi.io, by changing *only* Infrastructure. (We literally did the latter — see Section 5.)

### `AttendanceSystem.Api` — the entry point, not the brain
**Contains:** controllers, the global exception middleware, JWT configuration, rate limiting,
health checks, Swagger, and `Program.cs` (the composition root that wires everything together).

**Why it is not the "brain":** controllers are deliberately thin. A controller's job is to
translate HTTP into a MediatR request and translate the result (or exception) back into HTTP.
The actual decision-making lives in the Application handlers and the Domain entities. If you
later added a gRPC interface or a CLI, you would add a new outer adapter and reuse all the
Application/Domain logic untouched.

### `AttendanceSystem.UnitTests`
Fast, in-memory tests with **no I/O**. They test Domain rules (e.g. `RecordClockOut` throws when
clock-out ≤ clock-in) and Application handlers (with mocked repositories and a mocked time
service). They prove the *logic* is correct in isolation.

### `AttendanceSystem.IntegrationTests`
End-to-end tests that boot the real API in-process (`WebApplicationFactory<Program>`) against a
**real SQL Server** database, exercising the full HTTP → MediatR → EF Core → DB path. They prove
the *pieces work together* (routing, auth, persistence, the background job). They are separate
from unit tests because they are slower and require a database.

## 2.3 The dependency diagram (who references whom)

```
                         ┌───────────────────────────┐
                         │  AttendanceSystem.Api      │
                         │  (controllers, Program.cs) │
                         └────────────┬──────────────-┘
                            ┌─────────┴───────────┐
                            ▼                     ▼
        ┌──────────────────────────────┐   ┌───────────────────────────────┐
        │ AttendanceSystem.Application  │◄──│ AttendanceSystem.Infrastructure│
        │ (use cases, interfaces)       │   │ (EF Core, time client, jobs)   │
        └───────────────┬──────────────┘   └───────────────┬───────────────-┘
                        │                                   │
                        ▼                                   ▼
                ┌──────────────────────────────────────────────────┐
                │            AttendanceSystem.Domain                 │
                │   (entities, value objects, exceptions) — no deps  │
                └──────────────────────────────────────────────────┘

  Tests:
    UnitTests        ──► Application, Domain, Infrastructure
    IntegrationTests ──► Api, Infrastructure (boots the whole app)
```

Reading the arrows: `Api → Application` and `Api → Infrastructure`; `Infrastructure → Application`;
`Application → Domain`. **Nothing points out of Domain.** Note that `Api → Infrastructure` exists
only so the composition root can *register* the concrete implementations in DI — the controllers
themselves only ever use Application abstractions.

## 2.4 Thought experiment: what if a Domain entity imported an EF Core attribute?

Suppose `AttendanceLog` were decorated like this:

```csharp
// DON'T DO THIS
using Microsoft.EntityFrameworkCore;   // <-- Domain now depends on EF Core
[Index(nameof(EmployeeId))]
public class AttendanceLog { ... }
```

What breaks, concretely:

1. **The Domain `.csproj` now needs a `PackageReference` to EF Core.** The "zero dependencies"
   guarantee is gone. The Domain can no longer be reasoned about or tested without dragging in an
   ORM.
2. **Persistence concerns leak into business code.** Indexing is a *storage* decision. Putting it
   on the entity couples the meaning of "an attendance log" to one particular database technology.
3. **You can't swap the database.** Move to a document store or an in-memory store and those
   attributes are meaningless or wrong, yet they're welded to the core type.
4. **Test speed and purity suffer.** Domain unit tests would transitively load EF Core assemblies.

This is exactly why all persistence mapping lives in `AttendanceLogConfiguration` (an
`IEntityTypeConfiguration<AttendanceLog>` in Infrastructure), using EF Core's **Fluent API**. The
entity stays a clean C# class; the database knowledge stays in the outer layer. The Domain
`.csproj` is verified to contain no references — that is the architectural boundary made concrete.

---

# SECTION 3: Domain Layer — Line by Line

## 3.1 `Employee.cs`

```csharp
public class Employee
{
    public Guid Id { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string BadgeNumber { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = "Employee"; // Employee | Manager | Admin
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastModifiedAt { get; private set; }
    public string? RefreshToken { get; private set; }
    public DateTimeOffset? RefreshTokenExpiry { get; private set; }

    private readonly List<AttendanceLog> _attendanceLogs = new();
    public IReadOnlyCollection<AttendanceLog> AttendanceLogs => _attendanceLogs.AsReadOnly();

    private Employee() { }

    public static Employee Create(string fullName, string email, string badgeNumber, string passwordHash, string role = "Employee")
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("FullName is required", nameof(fullName));
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));
        if (string.IsNullOrWhiteSpace(badgeNumber)) throw new ArgumentException("BadgeNumber is required", nameof(badgeNumber));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("PasswordHash is required", nameof(passwordHash));

        return new Employee
        {
            Id = Guid.NewGuid(),
            FullName = fullName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            BadgeNumber = badgeNumber.Trim().ToUpperInvariant(),
            PasswordHash = passwordHash,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Deactivate() { IsActive = false; LastModifiedAt = DateTimeOffset.UtcNow; }
    public void Activate()   { IsActive = true;  LastModifiedAt = DateTimeOffset.UtcNow; }

    public void UpdateRefreshToken(string token, DateTimeOffset expiry)
    {
        RefreshToken = token;
        RefreshTokenExpiry = expiry;
        LastModifiedAt = DateTimeOffset.UtcNow;
    }

    public void ClearRefreshToken()
    {
        RefreshToken = null;
        RefreshTokenExpiry = null;
        LastModifiedAt = DateTimeOffset.UtcNow;
    }

    public bool HasValidRefreshToken(string token) =>
        RefreshToken == token && RefreshTokenExpiry > DateTimeOffset.UtcNow;
}
```

**Every property uses `private set`.** Notice there is no public setter anywhere. You cannot write
`employee.Email = "x"` from outside the class. This is **encapsulation**: the only ways to change
an `Employee` are the methods the class exposes (`Create`, `Deactivate`, `UpdateRefreshToken`,
etc.). This guarantees the entity can never enter an invalid state, because every mutation goes
through code that enforces the rules. EF Core can still populate these properties when reading from
the database — it sets private setters via reflection.

**Why is the constructor private (`private Employee() { }`)?**
EF Core needs a parameterless constructor to materialize entities when reading rows. But we do not
want application code to do `new Employee()` and get a blank, invalid object (empty name, empty
email). Making the constructor `private` means *only* EF Core (via reflection) and the class's own
static factory can use it. Everyone else must go through `Create`.

**Why a static factory `Create(...)` instead of a public constructor?**
- It has a **name** that documents intent (`Employee.Create(...)` reads better than `new Employee(...)`).
- It can **validate** and throw before an object exists — the four `IsNullOrWhiteSpace` guards mean
  you can never construct an employee with a missing required field.
- It can **normalize** input: `Email` is trimmed and lower-cased; `BadgeNumber` is trimmed and
  upper-cased. This guarantees consistent, comparable values (so `GetByEmailAsync` can match
  reliably regardless of how the caller cased the input).
- It can **assign system-controlled fields**: `Id = Guid.NewGuid()` and `CreatedAt =
  DateTimeOffset.UtcNow`. (Note: `CreatedAt` is an audit/bookkeeping timestamp, not an attendance
  timestamp, so using `UtcNow` here is fine — the "external time only" rule applies specifically to
  clock-in/out instants.)

**`IReadOnlyCollection<AttendanceLog> AttendanceLogs` backed by a private `List`.**
The navigation collection is exposed as **read-only**. External callers can enumerate an employee's
logs but cannot `.Add()` or `.Clear()` them — the backing `_attendanceLogs` field is private. This
prevents code elsewhere from bypassing the rules by mutating the collection directly. EF Core knows
to use the backing field by convention.

**Why is `PasswordHash` on the domain entity?**
Authentication is part of *who an employee is* in this system; the employee aggregate owns its
credential. Critically, it is the **hash**, never the plain password — the domain never sees or
stores a raw password. Hashing happens at the edge (seeding / a future registration flow) using
BCrypt, and only the resulting hash reaches the entity.

**`UpdateRefreshToken(token, expiry)` — why store expiry alongside the token?**
A refresh token is only valid for a window (7 days by default). Storing the expiry **with** the
token lets `HasValidRefreshToken` answer "is this token still usable?" with a single check:
`RefreshToken == token && RefreshTokenExpiry > DateTimeOffset.UtcNow`. Both the identity *and* the
freshness are verified together. (Again, `UtcNow` here governs a *security token lifetime*, not an
attendance record — that is an appropriate use of the system clock.) Note also that the stored
`RefreshToken` is itself a **hash** of the real token (the API layer hashes before calling this),
so a database leak does not expose usable refresh tokens.

## 3.2 `AttendanceLog.cs`

```csharp
public class AttendanceLog
{
    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }

    public DateTimeOffset ClockInUtc { get; private set; }
    public DateTimeOffset? ClockOutUtc { get; private set; }

    public string ClockInSource { get; private set; } = string.Empty;
    public string? ClockOutSource { get; private set; }

    public TimeSpan? Duration => ClockOutUtc.HasValue ? ClockOutUtc.Value - ClockInUtc : null;

    public bool IsAutoTimeout { get; private set; }
    public bool IsManualCorrection { get; private set; }

    public Guid? CorrectedByEmployeeId { get; private set; }
    public string? CorrectionNotes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastModifiedAt { get; private set; }

    public bool IsOpen => !ClockOutUtc.HasValue;

    private AttendanceLog() { }

    public static AttendanceLog CreateClockIn(Guid employeeId, DateTimeOffset clockInUtc, string source) { ... }
    public void RecordClockOut(DateTimeOffset clockOutUtc, string source) { ... }
    public void ApplyAutoTimeout(DateTimeOffset timeoutUtc) { ... }
    public void ApplyManualCorrection(DateTimeOffset? newClockIn, DateTimeOffset? newClockOut, Guid correctedBy, string notes) { ... }
}
```

**Why is `ClockInUtc` a `DateTimeOffset` and not a `DateTime`?**
This deserves a thorough answer because it is central to the whole system.

- A `DateTime` is ambiguous. Its `Kind` can be `Utc`, `Local`, or `Unspecified`, and that kind is
  easily lost across serialization, database round-trips, and API boundaries. A bare `DateTime` of
  `2025-01-15 09:00:00` does not tell you *which* 09:00 — Zurich? UTC? The server's local zone?
- A `DateTimeOffset` is **unambiguous**: it stores the wall-clock time **plus the offset from UTC**
  at that instant — e.g. `2025-01-15T09:00:00+01:00`. From it you can always compute the exact UTC
  instant, *and* you can see the local offset that was in effect.
- **DST (Daylight Saving Time) makes this essential.** Europe/Zurich is `+01:00` in winter and
  `+02:00` in summer. A shift that starts at 09:00 in January carries `+01:00`; one in July carries
  `+02:00`. With `DateTimeOffset`, the offset for *that specific instant* is captured, so
  subtraction (`ClockOutUtc - ClockInUtc`) is always a correct elapsed duration, even across a DST
  transition. A naive `DateTime` subtraction across the spring-forward boundary would be off by an
  hour.

So: we **store the offset-bearing instant**, which is timezone-correct and DST-safe, and we
*render* Europe/Zurich wall-clock time at the edges (the frontend uses `Intl.DateTimeFormat` with
`timeZone: 'Europe/Zurich'`). Storage is unambiguous UTC-anchored; display is human-friendly local.

**`ClockOutUtc` is nullable (`DateTimeOffset?`).** A `NULL` clock-out is the data representation of
"this session is still open." This nullable column is also what the filtered index and the
"one open session" rule key off of (see `IsOpen` below and Section 9).

**`Duration` is a computed property, not a stored column.**
```csharp
public TimeSpan? Duration => ClockOutUtc.HasValue ? ClockOutUtc.Value - ClockInUtc : null;
```
It is derived from two fields that are *already* stored. Storing it as well would be **denormalized
and dangerous**: if someone corrected `ClockOutUtc` but forgot to recompute `Duration`, the two
would disagree and you would not know which to trust. By computing it on demand, there is a single
source of truth and the duration can never drift out of sync. It is `null` while the session is
open (no end time yet). In Section 5 you'll see `builder.Ignore(a => a.Duration)` telling EF Core
*not* to try to map this to a column.

**`IsOpen` is a computed property too.**
```csharp
public bool IsOpen => !ClockOutUtc.HasValue;
```
"Open" is not an independent piece of state that could disagree with reality — it is *defined as*
"has no clock-out." Computing it from `ClockOutUtc` makes an illegal state (e.g. `IsOpen == true`
while `ClockOutUtc` is set) literally unrepresentable.

**`RecordClockOut` — why throw if `clockOutUtc <= ClockInUtc`?**
```csharp
public void RecordClockOut(DateTimeOffset clockOutUtc, string source)
{
    if (!IsOpen)
        throw new InvalidOperationException($"Attendance log {Id} is already closed.");
    if (string.IsNullOrWhiteSpace(source))
        throw new ArgumentException("Clock-out source is required", nameof(source));
    if (clockOutUtc <= ClockInUtc)
        throw new ArgumentException("Clock-out time must be after clock-in time.", nameof(clockOutUtc));

    ClockOutUtc = clockOutUtc;
    ClockOutSource = source;
    LastModifiedAt = DateTimeOffset.UtcNow;
}
```
A shift cannot end before (or exactly when) it began — a negative or zero duration is nonsensical
and would poison payroll math. The entity refuses to enter that state. It *also* refuses to clock
out a session that is already closed (`!IsOpen`). These are **invariants** enforced at the lowest
level, so no caller — handler, test, or future code — can ever violate them. The same rule is
mirrored as a database `CHECK` constraint (Section 9), giving defense in depth.

**`IsAutoTimeout` — when is it `true`?**
Set by `ApplyAutoTimeout`, which the background job calls for sessions left open past the threshold
(default 16h). It marks "the *system* closed this, the employee did not." That distinction matters
to a manager reviewing history: an auto-closed shift's end time is an estimate, not a real
clock-out, and may need correction.

**`IsManualCorrection` — why it matters for auditing.**
Set by `ApplyManualCorrection`. It flags "an administrator changed the recorded times." Combined
with `CorrectedByEmployeeId` and `CorrectionNotes` (and a full before/after `AuditLog` row), it
creates an unforgeable trail: *who* changed *what*, *when*, and *why*. In a payroll/compliance
system, "the record was edited" must never be invisible. `ApplyManualCorrection` validates the
*effective* result (merging any new values with existing ones) so a single-field edit can't produce
clock-out ≤ clock-in.

## 3.3 `ZurichTime.cs` — a Value Object

```csharp
public sealed class ZurichTime : IEquatable<ZurichTime>
{
    public DateTimeOffset Value { get; }
    public string Source { get; }            // API URL that provided this time
    public DateTimeOffset ReceivedAtUtc { get; } // when WE received it

    private ZurichTime(DateTimeOffset value, string source, DateTimeOffset receivedAt)
    {
        Value = value; Source = source; ReceivedAtUtc = receivedAt;
    }

    public static ZurichTime FromApiResponse(DateTimeOffset apiTime, string apiSource)
    {
        if (string.IsNullOrWhiteSpace(apiSource)) throw new ArgumentException("Source is required");
        return new ZurichTime(apiTime, apiSource, DateTimeOffset.UtcNow);
    }

    public bool Equals(ZurichTime? other) { ... return Value.Equals(other.Value); }
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(ZurichTime? left, ZurichTime? right) { ... }
    public static bool operator !=(ZurichTime? left, ZurichTime? right) => !(left == right);
    public override string ToString() => $"{Value:O} (from {Source})";
}
```

**What is a Value Object, and how does it differ from an Entity?**
- An **Entity** has an identity that persists over time. Two employees with identical names are
  still *different* employees because they have different `Id`s. `Employee` and `AttendanceLog` are
  entities.
- A **Value Object** has *no* identity; it is defined entirely by its values. Two `ZurichTime`s
  representing the same instant are interchangeable — like how two `5`s are equal. Value objects are
  **immutable** (all properties here are get-only, set once in a private constructor) and compared
  **by value**, not by reference.

**Why does `ZurichTime` exist instead of just passing a `DateTimeOffset`?**
It bundles three facts that belong together and gives them meaning:
1. `Value` — the authoritative instant.
2. `Source` — *where it came from* (the time API URL). This is the **provenance** an auditor needs:
   not just "what time," but "according to which trusted source."
3. `ReceivedAtUtc` — when *our system* received the value, which lets you reason about staleness
   (e.g. a cached value's age).

A bare `DateTimeOffset` carries only #1. By introducing a value object, the type system itself
communicates "this is an authoritative external time, with provenance," and it is impossible to
accidentally pass a server-clock `DateTimeOffset` where a `ZurichTime` is required. The factory
`FromApiResponse` is the only way to make one, and it stamps `ReceivedAtUtc` automatically.

**Equality is by `Value` only.** `Equals`, `GetHashCode`, and `==`/`!=` all consider only `Value`,
deliberately ignoring `Source` and `ReceivedAtUtc`. Two `ZurichTime`s that represent the same
instant are equal even if one came from the cache and another from a fresh call. This is the
correct value semantics for "the same moment in time."

## 3.4 Domain Exceptions

The Domain defines exceptions that express **business failures**, each carrying the data the upper
layers need to react. They live in `AttendanceSystem.Domain.Exceptions`.

```csharp
public class AlreadyClockedInException : Exception
{
    public Guid EmployeeId { get; }
    public DateTimeOffset ExistingClockInTime { get; }
    public AlreadyClockedInException(Guid employeeId, DateTimeOffset existingClockInTime)
        : base($"Employee {employeeId} already has an active clock-in session started at {existingClockInTime:O}")
    { EmployeeId = employeeId; ExistingClockInTime = existingClockInTime; }
}

public class NotClockedInException : Exception
{
    public Guid EmployeeId { get; }
    public NotClockedInException(Guid employeeId)
        : base($"Employee {employeeId} does not have an active clock-in session")
    { EmployeeId = employeeId; }
}

public class TimeServiceUnavailableException : Exception
{
    public string? Reason { get; }
    private const string DefaultMessage =
        "External time service is unavailable. Clock-in/out operations require an accurate time source.";
    public TimeServiceUnavailableException() : base(DefaultMessage) { }
    public TimeServiceUnavailableException(string? reason) : base(DefaultMessage) { Reason = reason; }
    public TimeServiceUnavailableException(string? reason, Exception innerException)
        : base(DefaultMessage, innerException) { Reason = reason; }
}

public class EmployeeNotFoundException : Exception
{
    public string IdentifierType { get; }
    public string Identifier { get; }
    public EmployeeNotFoundException(string identifierType, string identifier)
        : base($"Employee with {identifierType} '{identifier}' was not found")
    { IdentifierType = identifierType; Identifier = identifier; }
}

public class InvalidShiftException : Exception
{
    public string RuleViolated { get; }
    public InvalidShiftException(string ruleViolated, string message) : base(message) { RuleViolated = ruleViolated; }
    public InvalidShiftException(string ruleViolated, string message, Exception innerException)
        : base(message, innerException) { RuleViolated = ruleViolated; }
}
```

| Exception | Meaning | HTTP status it maps to |
|-----------|---------|------------------------|
| `AlreadyClockedInException` | Tried to clock in while a session is open | `409 Conflict` |
| `NotClockedInException` | Tried to clock out with no open session | `400 Bad Request` |
| `TimeServiceUnavailableException` | The external time source could not be reached | `503 Service Unavailable` |
| `EmployeeNotFoundException` | No employee for the given id/email/badge | `404 Not Found` |
| `InvalidShiftException` | A business rule (e.g. shift too long) was violated | `422 Unprocessable Entity` |

**Why do exceptions carry domain-specific properties?**
Consider `AlreadyClockedInException.ExistingClockInTime`. When the API turns this into a response,
it can tell the user *exactly* "you've been clocked in since 09:14." The exception is not just a
failure signal; it carries the *context* needed to produce a helpful, specific message. Similarly,
`EmployeeNotFoundException` carries which identifier type/value was searched, and
`TimeServiceUnavailableException` can carry a `Reason` and wrap the original network exception as
its `InnerException` (so the original cause is logged without leaking to the client).

**How do these propagate to the HTTP layer?**
The handlers (Application layer) simply `throw` them — they do **not** catch and convert to HTTP
codes, because the Application layer must not know about HTTP. Instead, the exception bubbles up
through the MediatR pipeline and out of the controller action. The `GlobalExceptionMiddleware`
(API layer) catches it, looks it up in its `ExceptionMap`, and produces the right status code and
an RFC 7807 `ProblemDetails` body. This keeps HTTP knowledge confined to the one place that should
have it (Section 6).

---

# SECTION 4: Application Layer — Line by Line

## 4.1 `ITimeService.cs`

```csharp
public interface ITimeService
{
    /// <summary>
    /// Gets the current time for Europe/Zurich from an external API.
    /// NEVER uses server local time or browser time.
    /// Throws TimeServiceUnavailableException if the API cannot be reached.
    /// </summary>
    Task<ZurichTime> GetCurrentTimeAsync(CancellationToken ct = default);

    /// <summary>Returns true if the time service is currently healthy (circuit closed).</summary>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}
```

**Why is this interface in Application and not Infrastructure?**
Because the Application *owns the contract* it depends on. This is Dependency Inversion: the use
cases declare "I need something that gives me the current Zurich time," and Infrastructure later
provides an implementation. If the interface lived in Infrastructure, the Application would have to
reference Infrastructure to use it — pointing the dependency arrow the wrong way and coupling the
core to the detail. With the interface here, Application depends only on its own abstraction.

**What the contract guarantees — and what it explicitly does not.**
The XML doc is part of the contract: it returns a `ZurichTime` (a value object carrying provenance)
sourced from an *external API*, and it **throws** `TimeServiceUnavailableException` if it cannot.
It explicitly does **not** offer any "give me an approximate time" or "use the server clock as a
fallback" capability. There is no method that returns a plain `DateTimeOffset` from the system
clock. The absence is intentional: the type system makes "accidentally use server time" impossible
within the use cases.

## 4.2 The persistence interfaces — Repository & Unit of Work

```csharp
public interface IAttendanceRepository
{
    Task<AttendanceLog?> GetActiveSessionAsync(Guid employeeId, CancellationToken ct = default);
    Task<AttendanceLog?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<AttendanceLog>> GetHistoryAsync(Guid employeeId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct = default);
    Task<int> GetHistoryCountAsync(Guid employeeId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);
    Task<IEnumerable<AttendanceLog>> GetAllActiveSessionsAsync(CancellationToken ct = default);
    Task<IEnumerable<AttendanceLog>> GetOpenSessionsOlderThanAsync(DateTimeOffset threshold, CancellationToken ct = default);
    Task AddAsync(AttendanceLog log, CancellationToken ct = default);
    void Update(AttendanceLog log);
}

public interface IEmployeeRepository
{
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Employee?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<Employee?> GetByBadgeNumberAsync(string badgeNumber, CancellationToken ct = default);
    Task<IEnumerable<Employee>> GetAllActiveAsync(CancellationToken ct = default);
    Task AddAsync(Employee employee, CancellationToken ct = default);
    void Update(Employee employee);
}

public interface IUnitOfWork
{
    IAttendanceRepository Attendance { get; }
    IEmployeeRepository Employees { get; }
    IAuditRepository Audits { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
}
```

**The Repository Pattern — what and why.**
A repository is an abstraction over data access that exposes **domain-meaningful operations**
(`GetActiveSessionAsync`, `GetOpenSessionsOlderThanAsync`) rather than raw query mechanics. Benefits:

- The Application speaks intent ("get the open session for this employee") instead of LINQ-against-EF
  details. The handler reads like the business process.
- It is **mockable**: unit tests substitute a fake `IAttendanceRepository` with `Moq` and never
  touch a database.
- It centralizes query logic (e.g. the `.AsNoTracking()` read optimizations, the filtered-index
  query) in one place in Infrastructure.

**The Unit of Work Pattern — what problem it solves.**
A single business operation often touches multiple repositories. Clocking in writes **both** an
`AttendanceLog` *and* an `AuditLog`. We need those two writes to be **atomic** — both succeed or
neither does. The Unit of Work groups the repositories and exposes a single `SaveChangesAsync()`
that commits all pending changes in one database transaction.

Contrast with calling `SaveChanges()` directly on a DbContext from each repository: you would get
*two* separate commits, and a crash between them would leave an attendance log with no audit trail
(or vice versa). The Unit of Work means the handler does all its work, then calls
`SaveChangesAsync()` **once** at the end, and EF Core wraps it in a transaction. (Under the hood,
all three repositories share the *same* `AppDbContext` instance, so EF's change tracker batches
every insert/update into that one `SaveChanges`.)

**Why are these interfaces in Application?** Same reason as `ITimeService`: the use cases declare
the persistence contract they need; Infrastructure implements it (`UnitOfWork`, `AttendanceRepository`,
…). The handlers depend on `IUnitOfWork`, never on `AppDbContext` or EF Core.

## 4.3 `ClockInCommand` + `ClockInCommandHandler` — the most important use case

```csharp
public record ClockInCommand(Guid EmployeeId, string? Notes = null) : IRequest<ClockInResult>;
```

A **command** is an immutable request to *change* state. It is a C# `record` (value-based,
concise) implementing MediatR's `IRequest<ClockInResult>`, which says "sending me yields a
`ClockInResult`." The handler:

```csharp
public async Task<ClockInResult> Handle(ClockInCommand request, CancellationToken ct)
{
    // Step 1: Verify employee exists and is active
    var employee = await _uow.Employees.GetByIdAsync(request.EmployeeId, ct)
        ?? throw new EmployeeNotFoundException("Id", request.EmployeeId.ToString());

    if (!employee.IsActive)
        throw new EmployeeNotFoundException("Id", request.EmployeeId.ToString());

    // Step 2: Check for existing open session (IDEMPOTENCY CHECK)
    var existingSession = await _uow.Attendance.GetActiveSessionAsync(request.EmployeeId, ct);
    if (existingSession != null)
        throw new AlreadyClockedInException(request.EmployeeId, existingSession.ClockInUtc);

    // Step 3: Get official time from external API (CRITICAL — no fallback to server clock)
    ZurichTime zurichTime;
    try
    {
        zurichTime = await _timeService.GetCurrentTimeAsync(ct);
    }
    catch (TimeServiceUnavailableException)
    {
        throw; // Re-throw, let middleware handle HTTP 503 — never substitute server time
    }

    // Step 4: Create attendance log
    var log = AttendanceLog.CreateClockIn(request.EmployeeId, zurichTime.Value, zurichTime.Source);

    // Step 5: Persist
    await _uow.Attendance.AddAsync(log, ct);

    // Step 6: Write audit log
    var audit = AuditLog.Create("AttendanceLog", log.Id, "ClockIn",
        null,
        JsonSerializer.Serialize(new { log.ClockInUtc, log.ClockInSource }),
        request.EmployeeId);
    await _uow.Audits.AddAsync(audit, ct);

    // Step 7: Save atomically
    await _uow.SaveChangesAsync(ct);

    _logger.LogInformation("Employee {EmployeeId} ({Name}) clocked in at {Time} via {Source}",
        employee.Id, employee.FullName, zurichTime.Value, zurichTime.Source);

    return new ClockInResult
    {
        LogId = log.Id,
        EmployeeId = employee.Id,
        ClockInUtc = log.ClockInUtc,
        TimeSource = zurichTime.Source,
        EmployeeName = employee.FullName
    };
}
```

**Step 1 — why verify the employee first?**
Fail fast and cheaply. If the employee doesn't exist or has been deactivated, there is no point
doing anything else (especially the expensive external time call). A deactivated employee is
treated the same as a missing one (`EmployeeNotFoundException`) — we deliberately do *not* reveal
"this account exists but is disabled."

**Step 2 — the idempotency check.**
"Idempotency" here means: clicking Clock-In when already clocked in should not create a second open
session. We look for an existing open session; if one exists, we throw `AlreadyClockedInException`
(carrying the existing clock-in time so the UI can say "you've been clocked in since…"). This is
the *first* line of defense for the "one open session" rule; the database unique filtered index is
the *last* line (for true race conditions — see Section 13, case 13).

**Step 3 — why the time comes from `ITimeService` and not `DateTime.UtcNow`.**
This is the heart of the system. We call the injected `ITimeService`, which (in production) reaches
the external Europe/Zurich API. If it throws `TimeServiceUnavailableException`, we **re-throw** —
we do not catch it and substitute `DateTime.UtcNow`. Using the server clock here would silently
record an unverified time into a compliance record; that is precisely the failure mode the entire
design exists to prevent. So the operation fails loudly (→ 503) and **no record is created**
(because we never reach `SaveChangesAsync`). The `try/catch` that only re-throws looks redundant,
but it documents intent at the exact decision point: *here is where one might be tempted to fall
back, and we deliberately refuse.*

**Step 4 — `AttendanceLog.CreateClockIn(...)`.**
The domain factory receives the employee id, the authoritative instant (`zurichTime.Value`), and
the provenance (`zurichTime.Source`). The entity, not the handler, decides how a valid clock-in log
is constructed (and validates its inputs).

**Step 5–6 — persist the log and write the audit in the same unit of work.**
`AddAsync(log)` stages the attendance row; `AuditLog.Create("AttendanceLog", log.Id, "ClockIn", …)`
records *what happened* — `null` old value (it's a creation), a JSON snapshot of the new value, and
*who* did it. Both are staged on the same `IUnitOfWork`.

**Step 7 — one `SaveChangesAsync()` at the end (atomicity).**
Calling save *once* at the end means the attendance log and its audit log commit together in a
single transaction. If anything fails, neither is written — you can never end up with an attendance
record that has no audit trail. This is the Unit of Work paying off.

The handler then logs (structured) and returns a `ClockInResult` DTO — a flat, serializable shape
for the HTTP response (it never returns the EF-tracked entity).

## 4.4 `ClockOutCommandHandler`

```csharp
public async Task<ClockOutResult> Handle(ClockOutCommand request, CancellationToken ct)
{
    var employee = await _uow.Employees.GetByIdAsync(request.EmployeeId, ct)
        ?? throw new EmployeeNotFoundException("Id", request.EmployeeId.ToString());
    if (!employee.IsActive)
        throw new EmployeeNotFoundException("Id", request.EmployeeId.ToString());

    var session = await _uow.Attendance.GetActiveSessionAsync(request.EmployeeId, ct)
        ?? throw new NotClockedInException(request.EmployeeId);

    var oldValueJson = JsonSerializer.Serialize(new { session.ClockInUtc, ClockOutUtc = (DateTimeOffset?)null, Status = "Open" });

    ZurichTime zurichTime;
    try { zurichTime = await _timeService.GetCurrentTimeAsync(ct); }
    catch (TimeServiceUnavailableException) { throw; }

    session.RecordClockOut(zurichTime.Value, zurichTime.Source);
    _uow.Attendance.Update(session);

    var newValueJson = JsonSerializer.Serialize(new {
        session.ClockInUtc, session.ClockOutUtc, session.ClockOutSource,
        DurationFormatted = DurationFormatter.FormatOrInProgress(session.Duration), Status = "Closed" });
    var audit = AuditLog.Create("AttendanceLog", session.Id, "ClockOut", oldValueJson, newValueJson, request.EmployeeId);
    await _uow.Audits.AddAsync(audit, ct);

    await _uow.SaveChangesAsync(ct);
    // ... returns ClockOutResult with LogId, ClockInUtc, ClockOutUtc, DurationFormatted ("Xh Ym")
}
```

Same shape as clock-in, with the important differences:

1. **Verify employee** (fail fast, identical reasoning).
2. **Find the active session** — if there is none, throw `NotClockedInException` → `400`. You can't
   end a shift you never started.
3. **Capture the "old value" JSON *before* mutating** — the audit records the open session as it was.
4. **Fetch official time** (same no-fallback contract).
5. **`session.RecordClockOut(...)`** — the entity enforces clock-out > clock-in and that the session
   was open. We then `Update(session)` (mark it modified in the change tracker).
6. **Audit with both old and new values**, so the change (open → closed, with computed duration) is
   fully reconstructable.
7. **One `SaveChangesAsync`** — atomic commit of the mutation + audit.

## 4.5 `ValidationBehavior.cs` — a MediatR pipeline behavior

```csharp
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (_validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken)));
            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
            if (failures.Count != 0)
                throw new ValidationException(failures); // carries ALL errors
        }
        return await next();
    }
}
```

**What is a MediatR pipeline behavior?**
It is middleware *for MediatR requests*. Every command/query passes through the registered
behaviors before reaching its handler, like layers of an onion: `Behavior1 → Behavior2 → Handler`.
`next()` invokes the next layer (eventually the handler). This lets us implement cross-cutting
concerns once and apply them to every request.

**Why collect ALL errors instead of stopping at the first?**
It injects *all* `IValidator<TRequest>` instances for the request type, runs them, and flattens
**every** failure into one `ValidationException`. If a request has three invalid fields, the user
sees all three at once, not one-at-a-time across three round-trips. Better UX, fewer requests.

**How FluentValidation integrates here.**
Validators (e.g. `ClockInCommandValidator : AbstractValidator<ClockInCommand>`) are registered in
DI by assembly scan. MediatR resolves the matching validators for `TRequest` and this behavior runs
them. If any fail, it throws `FluentValidation.ValidationException`, which the
`GlobalExceptionMiddleware` turns into a `400` with a structured `errors` map (field → messages).

## 4.6 `LoggingBehavior.cs`

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var employeeId = TryGetEmployeeId(request);
        _logger.LogInformation("Handling {RequestName} {EmployeeId}", requestName, employeeId is null ? "" : $"(EmployeeId: {employeeId})");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            stopwatch.Stop();
            _logger.LogInformation("Handled {RequestName} successfully in {ElapsedMs}ms", requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "{RequestName} failed after {ElapsedMs}ms: {Message}", requestName, stopwatch.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }
}
```

**What it logs and why:** the request name, the employee id (extracted reflectively if the request
has one), and — wrapping `next()` in a `Stopwatch` — the **duration** and **outcome** (success or
failure). This gives an operator a timing + audit trace of every use case for free, with zero code
in the handlers. Note it uses a `Stopwatch` (a monotonic timer for measuring elapsed time), which is
a legitimate use of system timing — it is *not* an attendance timestamp. On failure it logs a
warning with the exception and **re-throws** so the exception still reaches the middleware.

## 4.7 `GetAttendanceHistoryQueryHandler.cs` — pagination & formatting

```csharp
public record GetAttendanceHistoryQuery(Guid EmployeeId, DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<AttendanceHistoryDto>>;

public async Task<PagedResult<AttendanceHistoryDto>> Handle(GetAttendanceHistoryQuery request, CancellationToken ct)
{
    var page = request.Page < 1 ? 1 : request.Page;
    var pageSize = request.PageSize < 1 ? 20 : request.PageSize;

    var logs = await _uow.Attendance.GetHistoryAsync(request.EmployeeId, request.From, request.To, page, pageSize, ct);
    var totalCount = await _uow.Attendance.GetHistoryCountAsync(request.EmployeeId, request.From, request.To, ct);

    var items = logs.Select(log => new AttendanceHistoryDto
    {
        Id = log.Id,
        ClockInUtc = log.ClockInUtc,
        ClockOutUtc = log.ClockOutUtc,
        DurationFormatted = DurationFormatter.FormatDuration(log.ClockInUtc, log.ClockOutUtc),
        CrossesMidnight = DurationFormatter.CrossesMidnight(log.ClockInUtc, log.ClockOutUtc),
        IsOpen = log.IsOpen,
        IsManualCorrection = log.IsManualCorrection,
        IsAutoTimeout = log.IsAutoTimeout,
        ClockInSource = log.ClockInSource
    }).ToList();

    return new PagedResult<AttendanceHistoryDto> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
}
```

**What is pagination and why is it important here?**
A long-tenured employee could have thousands of attendance rows. Returning all of them in one
response is slow, memory-hungry, and useless to a UI that shows one page. Pagination fetches a
**window** — `page` and `pageSize` translate to SQL `OFFSET/FETCH` in the repository — plus a total
count so the client can render "Page 2 of 17." The handler also defends against bad input
(`page < 1`, `pageSize < 1`).

**How `PagedResult<T>` is constructed.**
```csharp
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; init; } = Enumerable.Empty<T>();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
```
It carries the page's `Items` plus the metadata needed for navigation. `TotalPages`,
`HasNextPage`, and `HasPreviousPage` are **computed** from the stored fields — the client gets
ready-to-use navigation flags without recomputing them.

**`DurationFormatted` and midnight crossing.**
`DurationFormatter.FormatDuration(clockIn, clockOut)` returns `"Xh Ym"`, appending
`" (crosses midnight)"` when clock-in and clock-out fall on **different Europe/Zurich calendar
days** (e.g. a 22:45 → 06:30 shift). `CrossesMidnight` is exposed as its own boolean so the UI can
badge such rows. Because the stored values are offset-bearing `DateTimeOffset`s, the formatter can
convert both ends to the Zurich zone (DST-aware via `TimeZoneInfo`) and compare calendar dates
correctly — see `DurationFormatter` in Section 5's neighborhood (it lives in Application/Common).

---

# SECTION 5: Infrastructure Layer — Line by Line

## 5.1 `AppDbContext.cs`

```csharp
public class AppDbContext : DbContext
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.Entity<Employee>().HasQueryFilter(e => e.IsActive);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges() { ApplyAuditTimestamps(); return base.SaveChanges(); }

    private void ApplyAuditTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified) continue;
            var lastModified = entry.Metadata.FindProperty("LastModifiedAt");
            if (lastModified is not null)
                entry.Property("LastModifiedAt").CurrentValue = DateTimeOffset.UtcNow;
        }
    }
}
```

**Why `ApplyConfigurationsFromAssembly` instead of configuring inline?**
Each entity's mapping lives in its own `IEntityTypeConfiguration<T>` class (e.g.
`AttendanceLogConfiguration`). `ApplyConfigurationsFromAssembly` scans the assembly and applies them
all. This keeps `OnModelCreating` tiny and each entity's configuration cohesive in its own file —
the same Single Responsibility benefit we get from per-handler classes. Inline configuration would
grow `OnModelCreating` into an unreadable monolith.

**The global query filter:** `HasQueryFilter(e => e.IsActive)` means every query against
`Employees` automatically appends `WHERE IsActive = 1`. Deactivated employees disappear from normal
queries (a "soft delete" behavior) without every query having to remember the filter. (Code that
*must* see inactive employees can call `.IgnoreQueryFilters()`.)

**How `SaveChangesAsync` auto-sets `LastModifiedAt`.**
Both `SaveChanges` overloads call `ApplyAuditTimestamps()` first. It walks the EF **change tracker**,
finds entries in the `Modified` state that have a `LastModifiedAt` property, and stamps them with
`DateTimeOffset.UtcNow` — writing through the change tracker (`entry.Property(...).CurrentValue`)
rather than the C# setter, which lets it set the value even though the entity's setter is `private`.
This is bookkeeping metadata (when the row was last touched), distinct from attendance timestamps,
so the system clock is appropriate here.

## 5.2 `AttendanceLogConfiguration.cs` — Fluent API mapping

```csharp
public void Configure(EntityTypeBuilder<AttendanceLog> builder)
{
    builder.ToTable("AttendanceLogs", t =>
        t.HasCheckConstraint("CK_ClockOut_After_ClockIn", "[ClockOutUtc] IS NULL OR [ClockOutUtc] > [ClockInUtc]"));

    builder.HasKey(a => a.Id);
    builder.Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

    builder.Property(a => a.EmployeeId).IsRequired();
    builder.Property(a => a.ClockInUtc).IsRequired().HasColumnType("datetimeoffset(7)");
    builder.Property(a => a.ClockOutUtc).HasColumnType("datetimeoffset(7)");
    builder.Property(a => a.ClockInSource).IsRequired().HasMaxLength(200);
    builder.Property(a => a.ClockOutSource).HasMaxLength(200);
    builder.Property(a => a.CorrectionNotes).HasMaxLength(500);
    builder.Property(a => a.CreatedAt).HasColumnType("datetimeoffset(7)");
    builder.Property(a => a.LastModifiedAt).HasColumnType("datetimeoffset(7)");

    builder.Ignore(a => a.Duration);
    builder.Ignore(a => a.IsOpen);

    builder.HasIndex(a => new { a.EmployeeId, a.ClockInUtc });

    builder.HasIndex(a => a.ClockOutUtc)
           .HasFilter("[ClockOutUtc] IS NULL")
           .HasDatabaseName("IX_AttendanceLogs_OpenSessions");

    builder.HasIndex(a => a.EmployeeId)
           .HasFilter("[ClockOutUtc] IS NULL")
           .IsUnique()
           .HasDatabaseName("UX_OneActiveSession");
}
```

Each Fluent API call explained:

- **`ToTable("AttendanceLogs", t => t.HasCheckConstraint(...))`** — names the table and attaches a
  database `CHECK` constraint (see below).
- **`HasKey(a => a.Id)`** — `Id` is the primary key.
- **`Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()")`** — if a row is inserted without
  an id, SQL Server generates a *sequential* GUID. (We usually supply the id from `Guid.NewGuid()`
  in the factory; the default is a safety net. See Section 9 for `NEWSEQUENTIALID` vs `NEWID`.)
- **`HasColumnType("datetimeoffset(7)")`** — maps `DateTimeOffset` to SQL Server's
  `DATETIMEOFFSET(7)` (offset-aware, 100-ns precision).
- **`IsRequired()`** — `NOT NULL`. `ClockInUtc` and `ClockInSource` are mandatory; `ClockOutUtc` is
  nullable (open session).
- **`HasMaxLength(200/500)`** — sizes the `nvarchar` columns instead of `nvarchar(max)`, which is
  smaller and indexable.
- **`Ignore(a => a.Duration)` / `Ignore(a => a.IsOpen)`** — tells EF Core these are **computed C#
  properties**, not columns. Without `Ignore`, EF would try to create `Duration`/`IsOpen` columns;
  we explicitly exclude them because they are derived (Section 3).

**What is a filtered index? Why does `WHERE ClockOutUtc IS NULL` make queries faster?**
```csharp
builder.HasIndex(a => a.ClockOutUtc).HasFilter("[ClockOutUtc] IS NULL")...
```
A filtered index indexes only the rows matching a predicate. Here it indexes only **open sessions**
(those with `ClockOutUtc IS NULL`). In a busy system, the vast majority of rows are *closed* (have
an end time); only a handful are open at any moment. The query "find the open session for employee
X" (`GetActiveSessionAsync`) and the auto-timeout sweep both target open rows. A filtered index over
just those is tiny and lightning fast, and it costs almost nothing to maintain because closed rows
aren't in it.

**The unique filtered index `UX_OneActiveSession`.**
```csharp
builder.HasIndex(a => a.EmployeeId).HasFilter("[ClockOutUtc] IS NULL").IsUnique()...
```
This is the database-level enforcement of "**at most one open session per employee**." It is unique
*only over open rows*, so an employee can have many closed sessions but never two open ones. This is
the race-condition backstop: if two clock-in requests slip past the in-handler check simultaneously,
the second `INSERT` violates this index and SQL Server rejects it. The middleware translates that
violation into `409 Conflict` (Section 6).

**The check constraint `CK_ClockOut_After_ClockIn` — why at the DB level, not just in code?**
The entity already refuses clock-out ≤ clock-in. But the database is the **last line of defense**:
it protects the data against bugs, ad-hoc SQL, migrations, or any future code path that bypasses the
entity. A `CHECK` constraint guarantees that *no matter how a row is written*, it can never have a
clock-out at or before its clock-in. Invariants this important are enforced in *both* the code (for
good error messages) and the database (for absolute integrity).

## 5.3 `WorldTimeApiClient.cs` — the typed HTTP client

> Architectural note that lives at the top of the file:
> `// ARCHITECTURE DECISION: This service is the ONLY source of time for attendance operations.`
> `// DateTime.Now, DateTime.UtcNow, and browser time are NEVER used for Clock-In/Out.`

```csharp
public async Task<WorldTimeApiResponse> GetZurichTimeAsync(CancellationToken ct = default)
{
    // timeapi.io: GET {BaseUrl}/Time/current/zone?timeZone=Europe/Zurich
    var url = $"{_settings.BaseUrl}/Time/current/zone?timeZone={_settings.Timezone}";
    _logger.LogDebug("Fetching time from {Url}", url);
    try
    {
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<WorldTimeApiResponse>(json)
            ?? throw new InvalidOperationException("Received null response from time API");
        _logger.LogInformation("Successfully fetched Zurich time: {Time} (DST: {Dst})", result.ParsedDateTime, result.Dst);
        return result;
    }
    catch (BrokenCircuitException ex)
    {
        throw new TimeServiceUnavailableException("Time service circuit breaker is open", ex);
    }
    catch (TimeoutRejectedException ex)
    {
        throw new TimeServiceUnavailableException($"Request timed out after {_settings.TimeoutSeconds} seconds", ex);
    }
    catch (HttpRequestException ex)
    {
        throw new TimeServiceUnavailableException($"HTTP error: {ex.Message}", ex);
    }
    catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
    {
        throw new TimeServiceUnavailableException($"Request timed out after {_settings.TimeoutSeconds} seconds", ex);
    }
    catch (JsonException ex)
    {
        throw new TimeServiceUnavailableException("Invalid response format from time API", ex);
    }
}
```

**The HTTP call and response shape.**
It is a **typed `HttpClient`** (registered via `AddHttpClient<WorldTimeApiClient>`), so the
`HttpClient` is managed by `IHttpClientFactory` (pooled handlers, no socket exhaustion). It GETs
`{BaseUrl}/Time/current/zone?timeZone=Europe/Zurich`. (The system was originally written against
`worldtimeapi.org`; the class name is historical. It now points at `timeapi.io` — see the note
below.) The JSON is deserialized into `WorldTimeApiResponse`.

**`ParsedDateTime` and tolerant parsing.**
`WorldTimeApiResponse.ParsedDateTime` turns the JSON into a `DateTimeOffset`. It supports two
provider shapes:
- worldtimeapi's `"datetime"` which **carries the UTC offset** (parsed with
  `DateTimeStyles.RoundtripKind`, which preserves the offset exactly rather than converting to local
  time).
- timeapi.io's `"dateTime"` which is **local Zurich wall-clock without an offset** — so the parser
  attaches the correct offset by asking `TimeZoneInfo` for Europe/Zurich's offset at that instant
  (DST-aware). This is why the type carries a `timeZone` field and resolves it.

```csharp
public DateTimeOffset ParsedDateTime
{
    get
    {
        if (!string.IsNullOrWhiteSpace(Datetime))
            return DateTimeOffset.Parse(Datetime, null, DateTimeStyles.RoundtripKind);
        if (!string.IsNullOrWhiteSpace(DateTimeLocal))
        {
            var local = DateTime.SpecifyKind(DateTime.Parse(DateTimeLocal, CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
            var tz = ResolveTimeZone(TimeZoneName);
            return new DateTimeOffset(local, tz.GetUtcOffset(local));
        }
        throw new FormatException("Time API response did not contain a recognizable datetime field.");
    }
}
```

**Why catch each exception and re-wrap as `TimeServiceUnavailableException`?**
The Application layer knows only the domain exception; it must not depend on `HttpRequestException`,
Polly's `BrokenCircuitException`, or `JsonException`. By translating *every* failure mode
(connection error, Polly timeout, open circuit, malformed JSON) into one
`TimeServiceUnavailableException` (preserving the original as `InnerException` for logs), the client
presents a clean, layer-appropriate contract. The handler catches one type; the middleware maps one
type to `503`. Note `TaskCanceledException` is only treated as a timeout *when the caller didn't
cancel* (`when (!ct.IsCancellationRequested)`) — a genuine caller cancellation is not a time-service
failure.

## 5.4 `CachedTimeService.cs` — the Decorator Pattern

```csharp
public class CachedTimeService : ITimeService
{
    private const string CacheKey = "zurich_time";
    private readonly WorldTimeApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly TimeApiSettings _settings;
    // ...
    public async Task<ZurichTime> GetCurrentTimeAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out ZurichTime? cached) && cached is not null)
            return cached;                                   // cache HIT

        var response = await _client.GetZurichTimeAsync(ct); // cache MISS → external call
        var source = $"{_settings.BaseUrl}/timezone/{_settings.Timezone}";
        var zurichTime = ZurichTime.FromApiResponse(response.ParsedDateTime, source);
        _cache.Set(CacheKey, zurichTime, TimeSpan.FromSeconds(_settings.CacheTtlSeconds));
        return zurichTime;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try { await GetCurrentTimeAsync(ct); return true; }
        catch (TimeServiceUnavailableException) { return false; }
        catch (Exception) { return false; }
    }
}
```

**What is the Decorator Pattern and how is it applied here?**
A decorator implements the *same interface* as the thing it wraps and adds behavior around it.
`CachedTimeService` implements `ITimeService` and wraps `WorldTimeApiClient`, adding a caching layer.
Callers depend on `ITimeService`; they have no idea a cache exists. We could stack more decorators
(e.g. a metrics decorator) without changing callers. DI registers `CachedTimeService` as the
`ITimeService` implementation in production.

**Why a 5-second TTL? What about 0 or 60?**
- The cache exists purely to absorb **bursts** — if ten employees clock in within the same second,
  we make **one** external call, not ten. This protects the fragile external dependency and keeps
  latency low.
- **TTL = 0** would disable caching: every clock-in hits the external API → more latency, more load,
  more exposure to outages.
- **TTL = 60** would be too long: a cached value could be up to a minute stale, so a clock-in's
  recorded time could be ~60s behind reality — unacceptable precision for attendance.
- **5 seconds** is the sweet spot: bursts are coalesced, yet the recorded time is never more than a
  few seconds old. The value is still *authoritative* — see next point.

**Cache hit vs. miss, and why a hit is still authoritative.**
On a **miss**, we call the external API, build a `ZurichTime`, and cache it for the TTL. On a
**hit**, we return the cached `ZurichTime`. Crucially, **the cached value originally came from the
external API** — caching is a performance optimization, not an alternate time source. We never
cache a server-clock value. A hit returns a real, recently-fetched authoritative instant; it does
not violate the "external source only" rule.

**`IsHealthyAsync`** simply tries to get the time and reports success/failure as a boolean — used by
the health check (Section 6) without throwing.

## 5.5 Polly: Circuit Breaker + Retry + Timeout

Registered in `TimeServicePollyExtensions.AddTimeServiceWithPolly`, the typed client is wrapped in
three composed policies (outer → inner: **retry → circuit breaker → timeout**):

```csharp
services.AddHttpClient<WorldTimeApiClient>(client =>
{
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds + 2); // outer safety net
})
.AddPolicyHandler((sp, _) => retry  ??= GetRetryPolicy(sp.GetRequiredService<ILoggerFactory>(), settings))
.AddPolicyHandler((sp, _) => breaker ??= GetCircuitBreakerPolicy(sp.GetRequiredService<ILoggerFactory>(), settings))
.AddPolicyHandler((sp, _) => timeout ??= GetTimeoutPolicy(settings));
```

**Circuit breaker — Closed / Open / Half-Open.**
A circuit breaker mimics an electrical breaker to stop hammering a failing dependency:
- **Closed** (normal): requests flow through. Failures are counted.
- **Open** (tripped): after **3 consecutive failures** (`CircuitBreakerThreshold`), the breaker
  *opens*. For the next **30 seconds** (`CircuitBreakerDurationSeconds`), requests **fail
  immediately** with `BrokenCircuitException` — no network call is even attempted. This gives the
  struggling time API room to recover and prevents our threads from piling up on slow/failing calls.
- **Half-Open** (probing): after the 30s, the breaker lets *one* trial request through. If it
  succeeds → back to **Closed**. If it fails → back to **Open** for another 30s.

```csharp
.CircuitBreakerAsync(
    handledEventsAllowedBeforeBreaking: settings.CircuitBreakerThreshold, // 3
    durationOfBreak: TimeSpan.FromSeconds(settings.CircuitBreakerDurationSeconds), // 30
    onBreak:    (_, _) => logger.LogError("Circuit breaker OPEN — time service unavailable"),
    onReset:    ()     => logger.LogInformation("Circuit breaker CLOSED — time service recovered"),
    onHalfOpen: ()     => logger.LogWarning("Circuit breaker HALF-OPEN — testing time service"));
```

**Retry with exponential backoff.**
```csharp
.WaitAndRetryAsync(settings.RetryCount, attempt => TimeSpan.FromSeconds(0.5 * Math.Pow(2, attempt - 1)), ...)
```
Transient failures (a dropped packet, a momentary blip) often succeed on a quick retry. We retry up
to `RetryCount` (2) times, waiting **0.5s then 1.0s** — *exponential backoff* (each wait doubles).
Backoff avoids retrying instantly in a tight loop, which would add load to an already-struggling
service. Only transient errors and Polly timeouts are retried (`HandleTransientHttpError().Or<TimeoutRejectedException>()`).

**Timeout (Pessimistic, 3 seconds).**
```csharp
Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(settings.TimeoutSeconds), TimeoutStrategy.Pessimistic);
```
Why 3 seconds? Clock-in is on the user's interactive path — they are staring at a button. Waiting
30 seconds for a stuck time API is unacceptable; better to fail fast and let them retry. After 3s a
request is abandoned with `TimeoutRejectedException`. *Pessimistic* strategy enforces the timeout
even if the underlying HTTP call doesn't honor cancellation cooperatively. The `HttpClient.Timeout`
is set slightly higher (`TimeoutSeconds + 2`) as an outer safety net so Polly's per-try timeout is
the one that actually governs.

**`BrokenCircuitException` handling.** When the breaker is open, Polly throws
`BrokenCircuitException` *before* any HTTP call. `WorldTimeApiClient` catches it and re-wraps it as
`TimeServiceUnavailableException` (with the original as inner) — so the rest of the system sees one
consistent failure type, whether the cause was a timeout, a network error, or an open circuit.

## 5.6 `OpenSessionTimeoutJob.cs` — a hosted background service

```csharp
public class OpenSessionTimeoutJob : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Auto-timeout job iteration failed; will retry next cycle."); }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var autoTimeoutHours = _config.GetValue("AttendanceRules:AutoTimeoutHours", 16);
        var threshold = DateTimeOffset.UtcNow.AddHours(-autoTimeoutHours);

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var expiredSessions = (await uow.Attendance.GetOpenSessionsOlderThanAsync(threshold, ct)).ToList();

        foreach (var session in expiredSessions)
        {
            var oldJson = JsonSerializer.Serialize(new { session.ClockInUtc, session.ClockOutUtc, IsOpen = true });
            session.ApplyAutoTimeout(DateTimeOffset.UtcNow);
            uow.Attendance.Update(session);
            var audit = AuditLog.Create("AttendanceLog", session.Id, "AutoTimeout", oldJson,
                JsonSerializer.Serialize(new { session.ClockOutUtc, IsAutoTimeout = true }), null);
            await uow.Audits.AddAsync(audit, ct);
        }
        if (expiredSessions.Count > 0) await uow.SaveChangesAsync(ct);
        return expiredSessions.Count;
    }
}
```

**What is `IHostedService` / `BackgroundService`?**
ASP.NET Core can run long-lived background tasks alongside the web server. `BackgroundService` is a
base class with an `ExecuteAsync(stoppingToken)` you override; the host starts it on boot and signals
`stoppingToken` on shutdown for graceful termination.

**Why every hour?** Forgotten sessions are not urgent — an hourly sweep is plenty to catch "someone
left without clocking out." Running more often would add needless load; less often would let stale
sessions linger.

**The 16-hour threshold, and why it's configurable.** `AttendanceRules:AutoTimeoutHours` (default
16) defines "open longer than a plausible shift ⇒ the person forgot." 16h comfortably exceeds a long
shift but is well under 24h. It is configuration, not a constant, because different organizations
have different shift patterns — a hospital with 24h on-call shifts would raise it.

**Why `IServiceScopeFactory` instead of injecting `DbContext` directly?**
`AppDbContext` and the repositories are **scoped** services (one per HTTP request). A background
service is a **singleton** that lives for the whole app lifetime. Injecting a scoped `DbContext` into
a singleton is a classic bug — you'd capture one context forever, accumulating tracked entities and
risking thread-safety problems (a `DbContext` is not thread-safe). Instead, each run creates a fresh
scope (`_scopeFactory.CreateScope()`) and resolves a fresh `IUnitOfWork` from it, exactly like a web
request would. The scope (and its `DbContext`) is disposed when the `using` block ends.

**What `IsAutoTimeout = true` signals.** `ApplyAutoTimeout` sets the clock-out to "now," sets
`IsAutoTimeout = true`, and writes an `AuditLog` with action `"AutoTimeout"`. To a manager, the flag
says: *the system closed this, the employee didn't — the end time is an estimate and may need a
manual correction.* (`RunOnceAsync` is public so integration tests can trigger it without waiting an
hour.)

## 5.7 `DbSeeder.cs`

```csharp
public static async Task SeedAsync(AppDbContext context, CancellationToken ct = default)
{
    if (await context.Employees.AnyAsync(ct)) return; // already seeded

    var employees = new[]
    {
        Employee.Create("Anna Müller",  "anna@company.ch",  "EMP001", BCryptNet.HashPassword("Test@1234"), Roles.Employee),
        Employee.Create("Hans Weber",   "hans@company.ch",  "EMP002", BCryptNet.HashPassword("Test@1234"), Roles.Manager),
        Employee.Create("Admin System", "admin@company.ch", "ADM001", BCryptNet.HashPassword("Test@1234"), Roles.Admin),
    };
    await context.Employees.AddRangeAsync(employees, ct);
    await context.SaveChangesAsync(ct);
}
```

**Why seed on startup and not in a migration?**
Migrations describe **schema** (tables, columns, indexes) and are versioned, irreversible history.
Seed data is **runtime data** that may legitimately differ between environments and may evolve.
Putting demo users in a migration would bake them into the schema history and run them via raw SQL
(bypassing the domain factory and BCrypt). Seeding on startup runs through `Employee.Create` (so the
normalization and validation apply) and is **idempotent** — the `AnyAsync` guard means it only seeds
an empty database, so restarting the app never duplicates users.

**Why BCrypt-hash passwords, and what BCrypt is.**
Storing plain passwords is indefensible — a database leak would expose every credential, and people
reuse passwords across sites. We store a **one-way hash**: you can verify a password against it but
cannot recover the password from it. **BCrypt** is an *adaptive* password-hashing function built on
the Blowfish cipher. Two properties make it the right tool:
- **Per-password salt** (built in): identical passwords produce different hashes, defeating rainbow
  tables.
- **Tunable work factor (cost)**: BCrypt is *deliberately slow*, and you can dial up the cost as
  hardware gets faster. This makes brute-force guessing expensive. (General-purpose hashes like
  SHA-256 are *fast*, which is exactly wrong for passwords.) Login uses `BCrypt.Verify(plain, hash)`.

---

# SECTION 6: API Layer — Line by Line

## 6.1 `Program.cs` — the composition root

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config.ReadFrom.Configuration(context.Configuration));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRateLimiting();
builder.Services.AddHealthChecksConfiguration(builder.Configuration);
builder.Services.AddSwaggerWithJwt();
builder.Services.AddCorsForFrontend();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
        logger.LogInformation("Database migrated and seeded successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration/seed failed on startup. The API will start, but DB-backed endpoints will fail until the database is reachable.");
    }
}

app.UseSerilogRequestLogging();
app.UseMiddleware<GlobalExceptionMiddleware>();
if (!app.Environment.IsEnvironment("Testing"))
    app.UseHttpsRedirection();
app.UseCors(ApiServiceExtensions.CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Attendance API v1"));
}

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse });
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.RunAsync();

public partial class Program { } // exposed for WebApplicationFactory in integration tests
```

**The two halves.** Everything before `var app = builder.Build()` *registers services* (the DI
container). Everything after *builds the middleware pipeline* and maps endpoints. The
`Add*` extension methods (defined in `ServiceCollectionExtensions`, `HealthCheckExtensions`, and the
layer `DependencyInjection` classes) keep this file readable — each concern is one line.

**Serilog first** (`builder.Host.UseSerilog(...)`) reads its config from `appsettings.json` so logs
go to console + rolling file with the configured levels.

**Migrate + seed on startup.** `await db.Database.MigrateAsync()` applies any pending EF Core
migrations so the schema is current the moment the app starts (no manual step in dev). Then
`DbSeeder.SeedAsync(db)` ensures the demo users exist. This whole block runs inside a **scope**
(see below) and is wrapped in try/catch so a transient DB outage logs loudly instead of crashing the
host (the rest of the app still starts; only DB-backed endpoints fail until the DB is reachable).

**Why `using var scope = app.Services.CreateScope()`?**
`AppDbContext` is a **scoped** service — it is meant to live for one unit of work (an HTTP request),
not for the application's lifetime. At startup there is no HTTP request, so there is no ambient
scope. We must create one explicitly to resolve the scoped `AppDbContext`, use it, and dispose it
(the `using` ensures disposal). Resolving a scoped service straight from the root provider is an
error in ASP.NET Core's DI; creating a scope is the correct pattern.

**Middleware order — why it matters (a lot).** Middleware runs in the order added; each wraps the
next. The ordering here is deliberate:

| Order | Middleware | Why here |
|------:|-----------|----------|
| 1 | `UseSerilogRequestLogging` | Outermost, so it times/logs the *entire* request including everything below. |
| 2 | `GlobalExceptionMiddleware` | **Before** auth so it can catch exceptions thrown *anywhere* below — including in authn/authz — and render a clean ProblemDetails instead of a raw 500. |
| 3 | `UseHttpsRedirection` | Redirect to HTTPS early (skipped under the `Testing` env so the in-process test server's auth header isn't dropped on a cross-scheme redirect). |
| 4 | `UseCors` | **Before** auth/authorization: CORS preflight (`OPTIONS`) requests carry no credentials and must be answered before any auth runs; otherwise the browser blocks the real request. |
| 5 | `UseAuthentication` | Establishes *who* the caller is (validates the JWT, builds `User`). |
| 6 | `UseAuthorization` | Enforces *what* they may do (`[Authorize]`, roles). Must come **after** authentication — you can't authorize an unknown user. |
| 7 | `UseRateLimiter` | After auth so the limiter can partition by the authenticated user id. |

If `GlobalExceptionMiddleware` came *after* `UseAuthentication`, an exception during authentication
would escape it and produce an unformatted 500. If `UseCors` came *after* `UseAuthorization`, browser
preflight requests would be rejected before CORS headers were added. Order is correctness, not style.

**Endpoints.** Controllers are mapped, then three health endpoints (see 6.6). `public partial class
Program { }` at the bottom exposes the otherwise-internal top-level `Program` type so the integration
tests' `WebApplicationFactory<Program>` can boot the app in-process.

## 6.2 `GlobalExceptionMiddleware.cs`

```csharp
private static readonly Dictionary<Type, (int StatusCode, string Title)> ExceptionMap = new()
{
    [typeof(AlreadyClockedInException)]       = (409, "Already Clocked In"),
    [typeof(NotClockedInException)]           = (400, "Not Clocked In"),
    [typeof(TimeServiceUnavailableException)] = (503, "Time Service Unavailable"),
    [typeof(EmployeeNotFoundException)]       = (404, "Employee Not Found"),
    [typeof(InvalidShiftException)]           = (422, "Invalid Shift"),
    [typeof(ValidationException)]             = (400, "Validation Error"),
    [typeof(KeyNotFoundException)]            = (404, "Not Found"),
    [typeof(UnauthorizedAccessException)]     = (401, "Unauthorized"),
};

public async Task InvokeAsync(HttpContext context)
{
    try { await _next(context); }
    catch (Exception ex) { await HandleAsync(context, ex); }
}
```

**How it intercepts exceptions.** It is the second middleware, so it wraps the entire downstream
pipeline in a `try/catch`. Any exception thrown by a controller, a handler, EF Core, or auth bubbles
up to here.

**The `ExceptionMap`.** A dictionary from exception **type** to `(HTTP status, title)`. When an
exception is caught, the middleware looks up its type and produces the corresponding status. Unknown
exceptions default to `500`. There is also special handling: a `DbUpdateException` whose inner
`SqlException` is a unique-key violation (numbers 2601/2627) becomes `409` (the `UX_OneActiveSession`
race-condition backstop), while any other `DbUpdateException`/`DbException` is `500` (a real DB
problem) — **never** 503 (503 is reserved exclusively for the time service).

**RFC 7807 ProblemDetails.** Responses use the standard "problem details" JSON shape
(`Content-Type: application/problem+json`). Example for a double clock-in:

```json
{
  "type": "https://httpstatuses.io/409",
  "title": "Already Clocked In",
  "status": 409,
  "detail": "Employee c7cf79ca-... already has an active clock-in session started at 2026-06-07T17:04:45.2421481+02:00",
  "instance": "/api/attendance/clock-in"
}
```
A standardized error envelope means every client can parse failures uniformly; `ValidationException`
additionally fills an `errors` extension (field → messages array).

**Why `503` includes a `Retry-After` header.**
```csharp
if (ex is TimeServiceUnavailableException) context.Response.Headers.RetryAfter = "30";
```
`503` means "temporarily unavailable." `Retry-After: 30` tells a well-behaved client (and the UI)
"try again in 30 seconds" — which conveniently matches the circuit-breaker open window, so a retry
has a good chance of succeeding. It turns a raw failure into actionable guidance.

**Why stack traces never appear in production.**
```csharp
Detail = detailOverride ?? (statusCode >= 500 && !_env.IsDevelopment()
    ? "An internal error occurred. Please try again later." : ex.Message);
if (_env.IsDevelopment() && statusCode >= 500) problem.Extensions["stackTrace"] = ex.StackTrace;
```
Stack traces reveal internal structure (class names, file paths, library versions) that helps an
attacker and leaks implementation detail. In production, 5xx responses show a generic message; only
in Development do we attach the stack trace, where it aids debugging and never reaches end users.

## 6.3 `TokenService.cs`

```csharp
public string GenerateAccessToken(Employee employee)
{
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, employee.Id.ToString()),
        new(ClaimTypes.NameIdentifier, employee.Id.ToString()),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new("name", employee.FullName),
        new(ClaimTypes.Name, employee.FullName),
        new("email", employee.Email),
        new("role", employee.Role),
        new(ClaimTypes.Role, employee.Role),
        new("badge", employee.BadgeNumber),
    };
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
    var descriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(claims),
        Expires = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenExpiryMinutes),
        Issuer = _jwt.Issuer,
        Audience = _jwt.Audience,
        SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
    };
    return new JsonWebTokenHandler().CreateToken(descriptor);
}

public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
public string HashRefreshToken(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
public bool ValidateRefreshToken(string token, Employee employee) => employee.HasValidRefreshToken(HashRefreshToken(token));
```

**What claims are in the JWT and why.**
- `sub` / `ClaimTypes.NameIdentifier` = the employee id (the controller reads this to know *who* is
  calling — see `GetCurrentEmployeeId`).
- `jti` = a unique token id (a GUID) — uniquely identifies this token instance.
- `name` / `email` / `badge` = convenience identity claims for display.
- `role` / `ClaimTypes.Role` = the role, which drives `[Authorize(Roles = ...)]`.

Both the short JWT names *and* the .NET `ClaimTypes.*` URIs are emitted so the claims survive
regardless of inbound claim mapping (the JWT bearer options set `MapInboundClaims = false` and pin
`RoleClaimType`/`NameClaimType`). The token is signed with **HMAC-SHA256** using the secret — the
server can later verify it wasn't tampered with.

**`ClockSkew = TimeSpan.Zero` (set in `AddJwtAuthentication`).** By default, JWT validation allows
**5 minutes** of clock skew, so an "expired" token is still accepted for 5 minutes past `exp`. For a
15-minute access token that effectively makes it a 20-minute token. Setting skew to **zero** means
tokens expire *exactly* at `exp` — important for short-lived tokens and a tighter security posture.

**Refresh tokens & rotation.** `GenerateRefreshToken` returns 64 bytes of cryptographically secure
randomness (base64) — unguessable. We never store the raw token; `HashRefreshToken` (SHA-256) is
what lands on the `Employee`. `ValidateRefreshToken` hashes the presented token and compares (plus
checks expiry via `HasValidRefreshToken`). On every refresh, a **new** token is issued and the stored
hash replaced — *rotation* (see 6.4).

**Why short access tokens (15 min) and long refresh tokens (7 days)?**
The access token is sent on *every* request, so it has the largest exposure surface; keeping it
short (15 min) limits the damage window if it leaks. But forcing a full re-login every 15 minutes
would be miserable UX. The refresh token (long-lived, 7 days, sent only to `/refresh`) lets the
client silently obtain a fresh access token. Short-lived bearer + long-lived rotating refresh is the
standard balance of security and usability.

## 6.4 `AuthController.cs`

```csharp
[HttpPost("login")]
public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
{
    var employee = await _uow.Employees.GetByEmailAsync(request.Email, ct);
    if (employee is null || !employee.IsActive || !BCryptNet.Verify(request.Password, employee.PasswordHash))
    {
        _logger.LogWarning("Failed login attempt for {Email}", request.Email);
        return Unauthorized(new { message = "Invalid email or password." });
    }

    var accessToken = _tokens.GenerateAccessToken(employee);
    var rawRefresh = _tokens.GenerateRefreshToken();
    employee.UpdateRefreshToken(_tokens.HashRefreshToken(rawRefresh), _tokens.RefreshTokenExpiry);
    _uow.Employees.Update(employee);
    await _uow.SaveChangesAsync(ct);

    return Ok(new LoginResponse(accessToken, $"{employee.Id}:{rawRefresh}", _tokens.AccessTokenExpiry,
        new EmployeeSummary(employee.Id, employee.FullName, employee.Role, employee.BadgeNumber)));
}
```

**Login, line by line.** Look up the employee by (normalized) email. If not found, inactive, or the
password fails `BCrypt.Verify`, return `401`. Otherwise mint an access token, generate a refresh
token, store its **hash** + expiry on the employee, persist, and return both tokens + a small
employee summary. The refresh token returned to the client is `"{employeeId}:{rawRefresh}"` — the id
prefix lets `/refresh` locate the employee (the repository has no "find by token" method), while only
the random part's hash is stored server-side.

**Why "Invalid email or password" and not "Email not found"/"Wrong password"?**
This is **username enumeration prevention**. If the system said "email not found," an attacker could
probe which emails are registered (valuable for targeted phishing/credential-stuffing). If it said
"wrong password," it would *confirm* the email exists. A single generic message for *all* failure
modes leaks nothing about which accounts exist. (The server-side log still records the attempt for
operators.)

**Refresh token rotation — what it prevents.**
```csharp
[HttpPost("refresh")]
public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
{
    var parts = request.RefreshToken.Split(':', 2);
    if (parts.Length != 2 || !Guid.TryParse(parts[0], out var employeeId)) return Unauthorized(...);
    var employee = await _uow.Employees.GetByIdAsync(employeeId, ct);
    if (employee is null || !_tokens.ValidateRefreshToken(parts[1], employee)) return Unauthorized(...);

    var accessToken = _tokens.GenerateAccessToken(employee);
    var rawRefresh = _tokens.GenerateRefreshToken();
    employee.UpdateRefreshToken(_tokens.HashRefreshToken(rawRefresh), _tokens.RefreshTokenExpiry); // ROTATE
    _uow.Employees.Update(employee);
    await _uow.SaveChangesAsync(ct);
    return Ok(new RefreshResponse(accessToken, $"{employee.Id}:{rawRefresh}", _tokens.AccessTokenExpiry));
}
```
Each refresh issues a brand-new refresh token and overwrites the stored hash, so the **old refresh
token instantly stops working**. If a refresh token were stolen and the thief used it, the legitimate
user's next refresh would invalidate the thief's token (or vice versa), and the rightful user's
following request fails — surfacing the compromise instead of letting a stolen token be replayed
indefinitely. `logout` simply clears the stored token (`ClearRefreshToken`).

## 6.5 `AttendanceController.cs`

```csharp
private Guid GetCurrentEmployeeId()
{
    var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? throw new UnauthorizedAccessException("Invalid token");
    return Guid.Parse(sub);
}
```

**How `GetCurrentEmployeeId()` works.** After authentication middleware validated the JWT, the
caller's claims are on `HttpContext.User`. This helper reads the `NameIdentifier`/`sub` claim — the
employee id we packed into the token — and parses it to a `Guid`. The controller never trusts an id
from the request body for "who am I"; it always derives identity from the verified token.

```csharp
[HttpGet("history")]
public async Task<IActionResult> History([FromQuery] Guid? employeeId, [FromQuery] DateTimeOffset? from,
    [FromQuery] DateTimeOffset? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
{
    var currentId = GetCurrentEmployeeId();
    var targetId = employeeId ?? currentId;
    if (targetId != currentId && !IsManagerOrAdmin())
        return StatusCode(StatusCodes.Status403Forbidden, new { message = "You are not allowed to view another employee's history." });
    var result = await _mediator.Send(new GetAttendanceHistoryQuery(targetId, from, to, page, pageSize), ct);
    return Ok(result);
}
```

**Why an Admin can pass `?employeeId=` but an Employee cannot.** The target defaults to the caller.
If a caller asks for *someone else's* history (`targetId != currentId`) and is **not** a
Manager/Admin, they get `403 Forbidden`. A regular employee can only ever see their own data; a
Manager/Admin may view anyone's. Authorization is enforced server-side, not by hiding a button.

```csharp
[HttpPost("clock-out")]
[EnableRateLimiting(ApiServiceExtensions.ClockOperationsPolicy)]
public async Task<IActionResult> ClockOut([FromBody] ClockRequest? body, CancellationToken ct)
{
    var targetId = GetCurrentEmployeeId();
    if (body?.EmployeeId is Guid requested && requested != Guid.Empty && IsManagerOrAdmin())
        targetId = requested;
    var result = await _mediator.Send(new ClockOutCommand(targetId, body?.Notes), ct);
    return Ok(result);
}
```

**Admin force-clock-out.** Clock-out normally targets the caller. But if the body supplies an
`employeeId` **and** the caller is a Manager/Admin, the target becomes that employee — letting an
admin close someone else's forgotten session from the dashboard. A regular employee supplying an
`employeeId` is ignored (the role check fails), so they can only ever clock *themselves* out.

**`[EnableRateLimiting("clock-operations")]`.** Applies the named rate-limit policy (5 requests per
minute, partitioned per user) to the clock-in/clock-out actions. It throttles abuse/accidental
double-submits; the 6th request inside a minute gets `429 Too Many Requests`. (Clock-in carries the
same attribute.)

## 6.6 Health checks

Registered in `AddHealthChecksConfiguration`:
```csharp
services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sql-server", tags: new[] { "db", "sql", "ready" })
    .AddUrlGroup(new Uri("https://timeapi.io/api/Time/current/zone?timeZone=Europe/Zurich"), name: "time-api", tags: new[] { "external", "time-api", "ready" })
    .AddCheck<TimeServiceHealthCheck>("time-service", tags: new[] { "external", "ready" });
```

**What `GET /health` returns.** A JSON document (via `UIResponseWriter`) listing every check with its
status (`Healthy`/`Unhealthy`), duration, and tags. Overall status is `Unhealthy` (HTTP 503) if any
check fails, else `Healthy` (200). The three checks are: SQL Server reachability, the external time
API reachability, and the `TimeServiceHealthCheck` (which calls `ITimeService.IsHealthyAsync` —
respecting the cache/circuit breaker).

**Why check the time API in health matters.** The time API is a hard dependency of the core
feature. Surfacing its health means an operator/monitor can see "clock-in will fail" *before* users
report it — and a load balancer can route around an instance that can't reach the time source.

**`/health/live` vs `/health/ready`.** This is the Kubernetes liveness/readiness distinction:
- **Liveness** (`/health/live`, `Predicate = tags contains "live"`): "is the process alive and not
  deadlocked?" It deliberately includes *no* external dependencies — a failing database should not
  make the orchestrator kill and restart the pod (restarting won't fix the DB). (No checks are
  tagged `live`, so it reports healthy as long as the process responds.)
- **Readiness** (`/health/ready`, `Predicate = tags contains "ready"`): "can this instance serve
  traffic right now?" It includes the DB and time API. If they're down, the instance is pulled out
  of the load-balancer rotation until they recover — but the pod is not killed.

---

# SECTION 7: React Frontend — Line by Line

## 7.1 `client.ts` — the Axios instance with auto-refresh

```typescript
const client: AxiosInstance = axios.create({
  baseURL: import.meta.env.VITE_API_URL || 'https://localhost:5001',
  timeout: 10_000,
  headers: { 'Content-Type': 'application/json' },
});

client.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

let isRefreshing = false;
let failedQueue: Array<{ resolve: (token: string) => void; reject: (error: unknown) => void }> = [];

client.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as RetriableRequest | undefined;
    if (error.response?.status !== 401 || !original || original._retry || isAuthEndpoint(original.url))
      return Promise.reject(error);

    if (isRefreshing) {
      return new Promise((resolve, reject) => {
        failedQueue.push({
          resolve: (token: string) => { original.headers.Authorization = `Bearer ${token}`; resolve(client(original)); },
          reject,
        });
      });
    }
    original._retry = true;
    isRefreshing = true;
    try {
      await useAuthStore.getState().refreshAccessToken();
      const newToken = useAuthStore.getState().accessToken;
      if (!newToken) throw new Error('No access token after refresh');
      processQueue(null, newToken);
      original.headers.Authorization = `Bearer ${newToken}`;
      return client(original);
    } catch (refreshError) {
      processQueue(refreshError, null);
      await useAuthStore.getState().logout();
      if (typeof window !== 'undefined' && window.location.pathname !== '/login') window.location.assign('/login');
      return Promise.reject(refreshError);
    } finally {
      isRefreshing = false;
    }
  },
);
```

**What is an Axios interceptor?**
Interceptors are hooks that run on every request or response. They let you inject cross-cutting
behavior (like attaching auth headers, or handling 401s) in one place instead of in every API call —
the frontend equivalent of server-side middleware.

**Request interceptor — attach the JWT.** Before every request leaves, it reads the current access
token from the Zustand store and, if present, sets `Authorization: Bearer <token>`. Every API call is
authenticated automatically; individual call sites never deal with tokens.

**Response interceptor — handle 401.** A `401` means the access token is missing/expired. The
interceptor attempts a **transparent refresh**: call `/refresh`, get a new access token, and **retry
the original request** so the user never notices. Several guards prevent loops: it only acts on real
`401`s, never retries the same request twice (`_retry`), and never tries to refresh a `401` from the
auth endpoints themselves (`isAuthEndpoint`). If refresh fails, it logs out and redirects to
`/login`.

**The "failed queue" pattern — why it's needed.**
Imagine the dashboard fires three requests at once and all return `401` (token just expired). Without
coordination, all three would trigger three simultaneous `/refresh` calls — wasteful and racy (later
refreshes could invalidate earlier ones via rotation). The `isRefreshing` flag ensures **only the
first** `401` performs the refresh. The others push their `{resolve, reject}` into `failedQueue` and
*wait*. When the single refresh completes, `processQueue` resolves every queued request with the new
token, and each is retried. One refresh, many waiters — correct and efficient.

## 7.2 `authStore.ts` — Zustand, tokens in memory

```typescript
export const useAuthStore = create<AuthState>((set, get) => ({
  accessToken: null,
  refreshToken: null,
  employee: null,
  isAuthenticated: false,

  login: async (email, password) => {
    const { data } = await authApi.login(email, password);
    set({ accessToken: data.accessToken, refreshToken: data.refreshToken, employee: data.employee, isAuthenticated: true });
  },
  logout: async () => {
    try { if (get().accessToken) await authApi.logout(); } catch { /* best effort */ }
    set({ accessToken: null, refreshToken: null, employee: null, isAuthenticated: false });
  },
  refreshAccessToken: async () => {
    const current = get().refreshToken;
    if (!current) throw new Error('No refresh token available');
    const { data } = await authApi.refresh(current);
    set({ accessToken: data.accessToken, refreshToken: data.refreshToken });
  },
}));
```

**Why tokens in Zustand (in-memory) and not `localStorage`?**
This is a deliberate **security trade-off**. Tokens held only in JavaScript memory are *not*
persisted anywhere a script can later read them from disk.

**The XSS attack `localStorage` would enable.** If any cross-site-scripting vulnerability let an
attacker run JavaScript on the page (a malicious dependency, an unescaped user string), and tokens
lived in `localStorage`, that script could do `localStorage.getItem('token')` and **exfiltrate the
token** to an attacker's server — a full account takeover that outlives the session. Tokens in
memory are gone the instant the tab closes or reloads and are not sitting in a well-known storage key
for a script to grab. (XSS is still bad, but the blast radius is smaller and shorter-lived.)

**What happens on page refresh, and why it's acceptable.** A full reload clears JavaScript memory, so
the tokens vanish and the user must log in again. That is the *intended* cost of the in-memory
approach. For an internal attendance app this is a fine trade: a fresh login is mildly inconvenient;
a stolen long-lived token is a breach. (Refresh-token-in-httpOnly-cookie is an alternative with
different trade-offs; this codebase chose the simplest XSS-resistant option.)

## 7.3 `useAttendance.ts` — a React Query hook

```typescript
export function useAttendance() {
  const queryClient = useQueryClient();
  const showToast = useToastStore((s) => s.showToast);

  const statusQuery = useQuery({
    queryKey: ['attendance', 'status'],
    queryFn: () => attendanceApi.getStatus().then((r) => r.data),
    refetchInterval: 30_000,
    staleTime: 10_000,
  });

  const clockInMutation = useMutation({
    mutationFn: (notes?: string) => attendanceApi.clockIn(notes).then((r) => r.data),
    onSuccess: (data) => {
      showToast('success', `Clocked in at ${formatZurichTime(data.clockInUtc)} (Zurich time)`);
      queryClient.invalidateQueries({ queryKey: ['attendance'] });
    },
    onError: (error) => showToast('error', getErrorMessage(error)),
  });

  const clockOutMutation = useMutation({ /* same shape, success shows duration */ });

  return { status: statusQuery.data, isLoading: statusQuery.isLoading, clockIn: clockInMutation.mutateAsync,
    clockOut: clockOutMutation.mutateAsync, isClockingIn: clockInMutation.isPending, isClockingOut: clockOutMutation.isPending, /* ... */ };
}
```

**`useQuery`, `staleTime`, `refetchInterval`.**
`useQuery` fetches and caches *server state* under a **query key** (`['attendance', 'status']`).
- `refetchInterval: 30_000` — React Query refetches the status every 30 seconds, so the UI stays
  current (e.g. duration so far) without manual polling code.
- `staleTime: 10_000` — for 10 seconds after a fetch, the data is considered "fresh" and React Query
  won't refetch it on incidental re-renders/remounts. It balances freshness against chattiness.

**`useMutation` and cache invalidation.** Mutations *change* server state (clock in/out). On
`onSuccess`, `queryClient.invalidateQueries({ queryKey: ['attendance'] })` marks all attendance
queries stale, so the status query immediately refetches and the UI flips to the new state (e.g.
clocked-out → clocked-in) — no manual state juggling. `mutateAsync` returns a promise the component
can await.

**Handling 409 / 503 / network differently.** Errors flow into `onError`, which calls
`getErrorMessage(error)` (in `errorUtils.ts`). That helper inspects the Axios error and returns a
specific message: `409` → "You are already clocked in," `503` → "Time service unavailable. Please try
again in 30 seconds," **no response** (network/offline) → "No connection. Please check your
internet." Each failure mode yields a clear, distinct toast.

## 7.4 `ClockButton.tsx`

```typescript
export function ClockButton() {
  const { status, isLoading, clockIn, clockOut, isClockingIn, isClockingOut } = useAttendance();
  const { isOnline } = useNetworkStatus();
  const now = useCurrentTime(status?.currentZurichTime); // ticks every second from the backend time
  const [confirmOpen, setConfirmOpen] = useState(false);

  const isClockedIn = status?.isClockedIn ?? false;
  const pending = isClockingIn || isClockingOut;
  const disabled = !isOnline || pending || isLoading;
  // ... renders the live Zurich clock, the duration, and the green/red button + confirm dialog
}
```

And the ticking hook (`useCurrentTime.ts`):
```typescript
export function useCurrentTime(baselineIso?: string) {
  const [now, setNow] = useState<Date | null>(null);
  useEffect(() => {
    if (!baselineIso) return;
    let currentMs = new Date(baselineIso).getTime();
    setNow(new Date(currentMs));
    const id = setInterval(() => { currentMs += 1000; setNow(new Date(currentMs)); }, 1000);
    return () => clearInterval(id);
  }, [baselineIso]);
  return now;
}
```

**Visual states.** The button has several: **Loading** (status still fetching → spinner);
**Clocked out** (green "Clock In"); **Clocked in** (red "Clock Out" plus the live duration);
**Pending** (a clock action in flight → spinner, disabled); **Offline** (disabled with a hint).
Clicking opens a `ConfirmDialog` before the action runs.

**How the 1-second interval ticks the duration.** `useCurrentTime` takes the backend's
`currentZurichTime` as a **baseline**, seeds a local `Date`, and every second adds 1000ms and updates
state — a smooth client-side clock. The displayed duration is `now − clockInTime`, recomputed each
tick, so the "time since clock-in" counts up live.

**When the interval starts and stops.** The `useEffect` starts the interval whenever `baselineIso`
(the backend time) changes, and its cleanup (`clearInterval`) runs before the next effect or on
unmount — so each fresh backend value cleanly **restarts** the ticking from the corrected instant,
and there is never a leaked/duplicate interval.

**Why the duration is anchored to the backend `currentZurichTime`/`clockInTime`, not a local
`Date()`.** This is the frontend echo of the system's core rule. The user's machine clock may be
wrong or deliberately altered. By anchoring the displayed clock and duration to a value the *backend*
fetched from the authoritative time service, the UI shows trustworthy Zurich time and a duration
consistent with what will actually be recorded. The client tick is purely cosmetic smoothing between
the 30-second polls; the source of truth remains the backend.

## 7.5 `NetworkStatus.tsx` + `useNetworkStatus.ts`

```typescript
export function useNetworkStatus() {
  const [isOnline, setIsOnline] = useState(navigator.onLine);
  useEffect(() => {
    const handleOnline = () => setIsOnline(true);
    const handleOffline = () => setIsOnline(false);
    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);
    return () => { window.removeEventListener('online', handleOnline); window.removeEventListener('offline', handleOffline); };
  }, []);
  return { isOnline };
}
```

**How the `online`/`offline` events work.** The browser fires a global `online` or `offline` event on
`window` when connectivity changes. The hook seeds from `navigator.onLine` and subscribes to both
events, updating state; its cleanup unsubscribes to avoid leaks. `NetworkStatus.tsx` renders a red
banner ("⚠️ No internet connection…") whenever `isOnline` is false.

**Why disable the ClockButton when offline.** A clock-in/out *requires* a round trip to the backend
(which then calls the time service). Offline, that will fail. Disabling the button (`disabled =
!isOnline || ...`) gives immediate, honest feedback instead of letting the user click into a network
error — and prevents recording attempts that can't possibly succeed.

## 7.6 `ProtectedRoute.tsx`

```typescript
export function ProtectedRoute({ roles, children }: PropsWithChildren<ProtectedRouteProps>) {
  const { isAuthenticated, employee } = useAuth();
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  if (roles && employee && !roles.includes(employee.role)) return <Navigate to="/" replace />;
  return <>{children}</>;
}
```

**Authentication check.** If the user is not authenticated, it renders `<Navigate to="/login">` —
React Router redirects to the login page. The protected children never mount.

**Role authorization check.** If the route declares allowed `roles` (e.g. the admin route passes
`roles={['Admin','Manager']}`) and the current employee's role is not among them, it redirects to `/`
(home). So an Employee who types `/admin` is bounced back.

**Where it redirects on failure.** Unauthenticated → `/login`; wrong role → `/`. `replace` is used so
the failed URL doesn't pollute browser history. This is purely a **UX guard** — the real
authorization is enforced server-side (the API returns `403`); the frontend guard just avoids showing
pages the user can't use.

---

# SECTION 8: Full Request Flows

These traces follow a single request end-to-end through every layer.

## Flow A: User Logs In

```
Browser (LoginPage)                 ASP.NET Core API                         SQL Server
       │                                   │                                     │
   submit form                             │                                     │
       │  authApi.login(email,password)    │                                     │
       │  POST /api/auth/login ───────────►│ AuthController.Login                 │
       │                                   │  GetByEmailAsync(email) ───────────►│ SELECT … WHERE Email=@p
       │                                   │◄─────────────────── Employee row ───│
       │                                   │  BCrypt.Verify(pwd, hash)            │
       │                                   │  GenerateAccessToken (JWT, 15m)      │
       │                                   │  GenerateRefreshToken (64 rnd bytes) │
       │                                   │  UpdateRefreshToken(HASH, expiry)    │
       │                                   │  SaveChangesAsync ─────────────────►│ UPDATE Employees SET RefreshToken…
       │◄── 200 {accessToken,refreshToken, │                                     │
       │        employee} ─────────────────│                                     │
   authStore.login() stores tokens in memory (Zustand)                           │
   React Router navigate("/")                                                    │
```

1. The user submits the login form; `LoginPage` calls `authApi.login(email, password)`.
2. Axios issues `POST /api/auth/login`. (No token yet; the request interceptor attaches nothing.)
3. `AuthController.Login` receives the `LoginRequest`.
4. `IEmployeeRepository.GetByEmailAsync` queries the DB (parameterized) for the normalized email.
5. `BCrypt.Verify(password, employee.PasswordHash)` checks the password against the stored hash.
   On any failure (missing/inactive/bad password) → `401` with the generic message.
6. `TokenService.GenerateAccessToken` builds a JWT with the identity + role claims, signed HMAC-SHA256, `exp` = now + 15m.
7. `TokenService.GenerateRefreshToken` returns 64 cryptographically-random bytes (base64).
8. `Employee.UpdateRefreshToken` stores the **hash** of the refresh token + its 7-day expiry on the entity.
9. `IUnitOfWork.SaveChangesAsync` persists the updated employee row in one transaction.
10. Response: `{ accessToken, refreshToken: "{id}:{raw}", expiresAt, employee }`.
11. `authStore.login` puts the tokens + employee into Zustand (in-memory) and sets `isAuthenticated = true`.
12. React Router navigates to `/`; `ProtectedRoute` now allows the `EmployeePage` to render.

## Flow B: Employee Clicks "Clock In" (happy path)

```
ClockButton ─► ConfirmDialog ─► useAttendance.clockIn() ─► axios POST /api/attendance/clock-in (Bearer)
   │
   ▼
[API] UseAuthentication validates JWT → User has sub + role claims
[API] UseRateLimiter: this user < 5/min?  ── no ─► 429
   │ yes
   ▼
AttendanceController.ClockIn → GetCurrentEmployeeId() from JWT → mediator.Send(ClockInCommand)
   │
   ▼ MediatR pipeline
ValidationBehavior (ClockInCommandValidator) ─► LoggingBehavior (start Stopwatch) ─► ClockInCommandHandler
   │
   ├─ a. Employees.GetByIdAsync         → employee exists & active?
   ├─ b. Attendance.GetActiveSessionAsync → already open? (idempotency)
   ├─ c. ITimeService.GetCurrentTimeAsync():
   │       CachedTimeService → IMemoryCache["zurich_time"]?
   │         miss → WorldTimeApiClient.GetZurichTimeAsync()
   │             Polly: timeout(3s) ∘ circuit-breaker ∘ retry(2, backoff)
   │             HTTPS GET timeapi.io/api/Time/current/zone?timeZone=Europe/Zurich
   │             parse → ZurichTime.FromApiResponse(value, source) → cache 5s
   ├─ d. AttendanceLog.CreateClockIn(empId, zurichTime.Value, zurichTime.Source)
   ├─ e. Attendance.AddAsync(log)
   ├─ f. AuditLog.Create("ClockIn", …) → Audits.AddAsync(audit)
   └─ g. UnitOfWork.SaveChangesAsync()  → INSERT log + INSERT audit  (one transaction)
   │
   ▼
ClockInResult ─► (LoggingBehavior logs duration) ─► HTTP 200 JSON
   │
   ▼ [Browser]
React Query onSuccess → invalidateQueries(['attendance']) → status refetch → isClockedIn=true
ClockButton flips to red "Clock Out"; useCurrentTime starts the 1-second duration tick
```

Step-by-step:
1. User clicks the button → `ConfirmDialog` appears ("official time will be fetched from the
   Europe/Zurich time server").
2. User confirms → `useAttendance.clockIn()` (a `useMutation.mutateAsync`).
3. The mutation calls `attendanceApi.clockIn()`.
4. Axios `POST /api/attendance/clock-in`; the request interceptor adds `Authorization: Bearer <jwt>`.
5. `UseAuthentication` validates the JWT signature/issuer/audience/lifetime and populates `User`
   with the employee-id and role claims.
6. `UseRateLimiter` checks the `clock-operations` policy (≤ 5/min for this user). If exceeded → `429`.
7. `AttendanceController.ClockIn` runs; `GetCurrentEmployeeId()` reads the id from the JWT.
8. The controller sends a `ClockInCommand` through MediatR.
9. `ValidationBehavior` runs `ClockInCommandValidator` (e.g. non-empty employee id, notes length).
10. `LoggingBehavior` starts a `Stopwatch`.
11. `ClockInCommandHandler.Handle`:
    - a. `GetByIdAsync` — employee exists and is active (else `404`).
    - b. `GetActiveSessionAsync` — if already open → `AlreadyClockedInException` → `409`.
    - c. `ITimeService.GetCurrentTimeAsync()`:
      - `CachedTimeService` checks `IMemoryCache["zurich_time"]`.
      - **Miss** → `WorldTimeApiClient.GetZurichTimeAsync()`, wrapped in Polly (3s timeout, circuit
        breaker, 2 retries with backoff).
      - HTTPS GET the time API; parse the response into a `DateTimeOffset` (DST-correct offset).
      - `ZurichTime.FromApiResponse(value, source)`; cache it for 5 seconds.
    - d. `AttendanceLog.CreateClockIn(employeeId, zurichTime.Value, zurichTime.Source)`.
    - e. `Attendance.AddAsync(log)`.
    - f. `AuditLog.Create("AttendanceLog", log.Id, "ClockIn", null, <new JSON>, employeeId)` →
      `Audits.AddAsync`.
    - g. `UnitOfWork.SaveChangesAsync()` — the log and the audit commit together atomically.
12. The handler returns a `ClockInResult` up through MediatR.
13. `LoggingBehavior` logs the elapsed time + success.
14. The controller returns `200 OK` with the `ClockInResult` JSON.
15. React Query's `onSuccess` invalidates `['attendance']`.
16. The status query refetches → `isClockedIn = true`.
17. `ClockButton` switches to the red "Clock Out" state.
18. `useCurrentTime` (anchored to the backend time) ticks the duration counter every second.

## Flow C: Time API is Down During Clock-In

```
… steps 1–8 as Flow B …
   │
   ▼
ClockInCommandHandler → ITimeService.GetCurrentTimeAsync()
   CachedTimeService → cache MISS → WorldTimeApiClient
       Polly: try #1 → fail (timeout/conn) → wait 0.5s → try #2 → fail
       failure count hits threshold (3 across calls) → CIRCUIT OPENS
       (subsequent calls within 30s short-circuit with BrokenCircuitException)
   WorldTimeApiClient catches HttpRequestException / TimeoutRejectedException / BrokenCircuitException
       └─► throws TimeServiceUnavailableException   (NO server-clock fallback)
   │
   ▼
ClockInCommandHandler re-throws (does NOT catch-and-substitute)   ← SaveChangesAsync is NEVER reached
   │
   ▼
GlobalExceptionMiddleware catches TimeServiceUnavailableException
   ExceptionMap → 503 "Time Service Unavailable" + header Retry-After: 30
   ProblemDetails JSON body
   │
   ▼ [Browser]
axios response interceptor: status is 503 (not 401) → pass through (no refresh)
useMutation onError → getErrorMessage → toast: "Time service unavailable. Please try again in 30 seconds."
NO AttendanceLog row exists in the DB (the transaction was never started)
```

The defining property: **when the trusted clock is unavailable, nothing is written.** The handler
re-throws instead of falling back, so execution never reaches `SaveChangesAsync`. The middleware
returns `503` with `Retry-After: 30` (matching the circuit-breaker window). The UI shows a clear
retry message. The compliance record stays clean — no unverified timestamp is ever persisted. This
single behavior is the entire reason for the time service, the circuit breaker, and the
`TimeServiceUnavailableException` → `503` mapping.

---

# SECTION 9: Database Schema Reference

The schema is created by EF Core migrations (`InitialCreate` + `UniqueActiveSession`). The
equivalent SQL:

```sql
CREATE TABLE [Employees] (
    [Id]                 uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
    [FullName]           nvarchar(120)    NOT NULL,
    [Email]              nvarchar(256)    NOT NULL,
    [BadgeNumber]        nvarchar(30)     NOT NULL,
    [PasswordHash]       nvarchar(512)    NOT NULL,
    [Role]               nvarchar(20)     NOT NULL DEFAULT (N'Employee'),
    [IsActive]           bit              NOT NULL,
    [CreatedAt]          datetimeoffset(7) NOT NULL,
    [LastModifiedAt]     datetimeoffset(7) NULL,
    [RefreshToken]       nvarchar(512)    NULL,
    [RefreshTokenExpiry] datetimeoffset(7) NULL,
    CONSTRAINT [PK_Employees] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [IX_Employees_Email]       ON [Employees] ([Email]);
CREATE UNIQUE INDEX [IX_Employees_BadgeNumber] ON [Employees] ([BadgeNumber]);

CREATE TABLE [AttendanceLogs] (
    [Id]                   uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
    [EmployeeId]           uniqueidentifier NOT NULL,
    [ClockInUtc]           datetimeoffset(7) NOT NULL,
    [ClockOutUtc]          datetimeoffset(7) NULL,
    [ClockInSource]        nvarchar(200)    NOT NULL,
    [ClockOutSource]       nvarchar(200)    NULL,
    [IsAutoTimeout]        bit              NOT NULL,
    [IsManualCorrection]   bit              NOT NULL,
    [CorrectedByEmployeeId] uniqueidentifier NULL,
    [CorrectionNotes]      nvarchar(500)    NULL,
    [CreatedAt]            datetimeoffset(7) NOT NULL,
    [LastModifiedAt]       datetimeoffset(7) NULL,
    CONSTRAINT [PK_AttendanceLogs] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ClockOut_After_ClockIn] CHECK ([ClockOutUtc] IS NULL OR [ClockOutUtc] > [ClockInUtc]),
    CONSTRAINT [FK_AttendanceLogs_Employees_EmployeeId]
        FOREIGN KEY ([EmployeeId]) REFERENCES [Employees]([Id]) ON DELETE NO ACTION
);
CREATE INDEX        [IX_AttendanceLogs_EmployeeId_ClockInUtc] ON [AttendanceLogs] ([EmployeeId], [ClockInUtc]);
CREATE INDEX        [IX_AttendanceLogs_OpenSessions]          ON [AttendanceLogs] ([ClockOutUtc]) WHERE [ClockOutUtc] IS NULL;
CREATE UNIQUE INDEX [UX_OneActiveSession]                     ON [AttendanceLogs] ([EmployeeId]) WHERE [ClockOutUtc] IS NULL;

CREATE TABLE [AuditLogs] (
    [Id]                  bigint           NOT NULL IDENTITY(1,1),
    [EntityType]          nvarchar(50)     NOT NULL,
    [EntityId]            uniqueidentifier NOT NULL,
    [Action]              nvarchar(30)     NOT NULL,
    [OldValueJson]        nvarchar(max)    NULL,
    [NewValueJson]        nvarchar(max)    NULL,
    [ChangedByEmployeeId] uniqueidentifier NULL,
    [ChangedAt]           datetimeoffset(7) NOT NULL,
    [IpAddress]           nvarchar(45)     NULL,
    [UserAgent]           nvarchar(500)    NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [AuditLogs] ([EntityType], [EntityId]);
CREATE INDEX [IX_AuditLogs_ChangedAt]           ON [AuditLogs] ([ChangedAt]);
```

## 9.1 Column-by-column

**Employees**
| Column | Type | Notes |
|--------|------|-------|
| Id | `uniqueidentifier` | PK; default sequential GUID |
| FullName | `nvarchar(120)` | required |
| Email | `nvarchar(256)` | required, **unique**; normalized lower-case |
| BadgeNumber | `nvarchar(30)` | required, **unique**; normalized upper-case |
| PasswordHash | `nvarchar(512)` | BCrypt hash, never the password |
| Role | `nvarchar(20)` | default `Employee`; one of Employee/Manager/Admin |
| IsActive | `bit` | soft-delete flag; global query filter hides inactive |
| CreatedAt / LastModifiedAt | `datetimeoffset(7)` | bookkeeping timestamps |
| RefreshToken / RefreshTokenExpiry | `nvarchar(512)` / `datetimeoffset(7)` | hashed token + expiry, both nullable |

**AttendanceLogs**
| Column | Type | Notes |
|--------|------|-------|
| Id | `uniqueidentifier` | PK; default sequential GUID |
| EmployeeId | `uniqueidentifier` | FK → Employees, `ON DELETE NO ACTION` (Restrict) |
| ClockInUtc | `datetimeoffset(7)` | required; authoritative instant |
| ClockOutUtc | `datetimeoffset(7)` | **NULL = open session** |
| ClockInSource / ClockOutSource | `nvarchar(200)` | provenance of each timestamp |
| IsAutoTimeout / IsManualCorrection | `bit` | provenance flags |
| CorrectedByEmployeeId / CorrectionNotes | `uniqueidentifier` / `nvarchar(500)` | correction audit |
| CreatedAt / LastModifiedAt | `datetimeoffset(7)` | bookkeeping |

**AuditLogs** — append-only trail. `Id` is `bigint IDENTITY` (auto-increment); `EntityType` +
`EntityId` identify the changed row; `Action` is the verb (ClockIn/ClockOut/AutoTimeout/
ManualCorrection); `OldValueJson`/`NewValueJson` (`nvarchar(max)`) hold before/after snapshots;
`ChangedByEmployeeId`, `ChangedAt`, and optional `IpAddress`/`UserAgent` capture the actor/context.

## 9.2 `DATETIMEOFFSET(7)` vs `DATETIME2` vs `DATETIME`

| Type | Range / precision | Stores offset? | Verdict |
|------|-------------------|----------------|---------|
| `DATETIME` | ~3.33 ms precision, from 1753 | No | Legacy; imprecise; avoid. |
| `DATETIME2` | 100 ns precision | No | Precise, but **ambiguous timezone**. |
| `DATETIMEOFFSET(7)` | 100 ns precision | **Yes** (±14h offset) | **Chosen.** |

`DATETIMEOFFSET` is the only one that stores the **UTC offset** alongside the instant. That is
essential here: it preserves "this clock-in happened at 09:00 **+01:00**" unambiguously, makes
duration math correct across DST boundaries, and lets us both compute the exact UTC instant and
render Zurich wall-clock time. `(7)` is maximum fractional-second precision (100 ns).

## 9.3 `NEWSEQUENTIALID()` vs `NEWID()` — index fragmentation

- `NEWID()` produces **random** GUIDs. As the clustered primary key, each insert lands at a random
  position in the B-tree, causing **page splits** and heavy **index fragmentation** — slower inserts
  and bloated indexes.
- `NEWSEQUENTIALID()` produces **monotonically increasing** GUIDs, so new rows append at the *end* of
  the clustered index (like an identity column) — minimal page splits, far less fragmentation.

Since `Id` is the clustered PK, the column default uses `NEWSEQUENTIALID()` to keep inserts cheap and
the index healthy. (In practice we usually supply the id from `Guid.NewGuid()` in the factory; the
DB default is the safety net for direct inserts.)

## 9.4 The filtered unique index — "one active session per employee"

```sql
CREATE UNIQUE INDEX [UX_OneActiveSession] ON [AttendanceLogs] ([EmployeeId]) WHERE [ClockOutUtc] IS NULL;
```
This indexes only **open** rows (`ClockOutUtc IS NULL`) and makes `EmployeeId` **unique within that
set**. Consequences:
- An employee may have *many* closed sessions (history) — those are excluded by the `WHERE`.
- An employee may have **at most one** open session — a second open row for the same employee
  violates the unique index and the `INSERT` is rejected by SQL Server.
This is the ultimate guard for the idempotency rule, catching even true concurrent double-submits
that slip past the in-handler check (Section 13, case 13). The middleware maps the resulting unique
violation to `409 Conflict`.

---

# SECTION 10: Complete File Map

### Backend — `AttendanceSystem.Domain`
| File | Responsibility | Key API | Depends on |
|------|----------------|---------|-----------|
| `Entities/Employee.cs` | Employee aggregate (identity, credential, refresh token) | `Create`, `Deactivate`, `UpdateRefreshToken`, `HasValidRefreshToken` | BCL only |
| `Entities/AttendanceLog.cs` | A clock-in/out session | `CreateClockIn`, `RecordClockOut`, `ApplyAutoTimeout`, `ApplyManualCorrection`, `IsOpen`, `Duration` | BCL only |
| `Entities/AuditLog.cs` | Immutable audit record | `Create` | BCL only |
| `ValueObjects/ZurichTime.cs` | Authoritative time + provenance (value object) | `FromApiResponse`, `Value`, `Source`, `ReceivedAtUtc` | BCL only |
| `Exceptions/*.cs` | Domain failures → HTTP codes | `AlreadyClockedIn/NotClockedIn/TimeServiceUnavailable/EmployeeNotFound/InvalidShift` | BCL only |
| `Constants/Roles.cs` | Role name constants | `Employee`, `Manager`, `Admin`, `ManagerOrAdmin` | BCL only |

### Backend — `AttendanceSystem.Application`
| File | Responsibility | Key API | Depends on |
|------|----------------|---------|-----------|
| `Common/Interfaces/ITimeService.cs` | Contract for authoritative time | `GetCurrentTimeAsync`, `IsHealthyAsync` | Domain |
| `Common/Interfaces/IAttendanceRepository.cs` | Attendance data access | `GetActiveSessionAsync`, `GetHistoryAsync`, `AddAsync`, `Update`… | Domain |
| `Common/Interfaces/IEmployeeRepository.cs` | Employee data access | `GetByEmailAsync`, `GetByIdAsync`, `Update`… | Domain |
| `Common/Interfaces/IAuditRepository.cs` | Audit writes/reads | `AddAsync`, `GetByEntityAsync` | Domain |
| `Common/Interfaces/IUnitOfWork.cs` | Atomic save + transactions | `SaveChangesAsync`, repos, `Begin/Commit/Rollback` | Domain interfaces |
| `Common/Behaviors/ValidationBehavior.cs` | MediatR validation pipeline | `Handle` | MediatR, FluentValidation |
| `Common/Behaviors/LoggingBehavior.cs` | MediatR logging/timing | `Handle` | MediatR, MS.Logging |
| `Common/DurationFormatter.cs` | "Xh Ym", midnight-crossing, Zurich tz | `Format`, `FormatDuration`, `CrossesMidnight` | BCL |
| `Common/Models/PagedResult.cs` | Generic paged response | `Items`, `TotalPages`, `HasNextPage`… | BCL |
| `Features/Attendance/Commands/ClockIn/*` | Clock-in use case | `ClockInCommand/Handler/Validator/Result` | Domain, interfaces, MediatR |
| `Features/Attendance/Commands/ClockOut/*` | Clock-out use case | `ClockOutCommand/Handler/Validator/Result` | as above |
| `Features/Attendance/Commands/ManualCorrection/*` | Admin correction | `ManualCorrectionCommand/Handler/Validator/Result` | as above |
| `Features/Attendance/Queries/GetHistory/*` | Paged history | `GetAttendanceHistoryQuery/Handler/Dto` | as above |
| `Features/Attendance/Queries/GetActiveEmployees/*` | Active sessions list | `GetActiveEmployeesQuery/Handler/Dto` | as above |
| `Features/Attendance/Queries/GetStatus/*` | Caller status + current Zurich time | `GetMyStatusQuery/Handler/Dto` | as above |
| `DependencyInjection.cs` | `AddApplication` (MediatR, validators, behaviors) | `AddApplication` | MediatR, FluentValidation DI |

### Backend — `AttendanceSystem.Infrastructure`
| File | Responsibility | Key API | Depends on |
|------|----------------|---------|-----------|
| `Persistence/AppDbContext.cs` | EF Core context, filter, audit stamping | `Employees/AttendanceLogs/AuditLogs`, `SaveChangesAsync` | EF Core, Domain |
| `Persistence/Configurations/*Configuration.cs` | Fluent mappings (tables, indexes, constraints) | `Configure` | EF Core |
| `Persistence/Repositories/*Repository.cs` | Repository implementations | per interface | EF Core, Application |
| `Persistence/UnitOfWork.cs` | Unit of Work over `AppDbContext` | `SaveChangesAsync`, transactions | EF Core, Application |
| `Persistence/DbSeeder.cs` | Idempotent seed (3 users, BCrypt) | `SeedAsync` | EF Core, BCrypt, Domain |
| `Persistence/DesignTimeDbContextFactory.cs` | EF CLI design-time context | `CreateDbContext` | EF Core |
| `ExternalServices/Time/ITimeService impls` | `WorldTimeApiClient`, `CachedTimeService`, `MockTimeService` | `GetZurichTimeAsync`, `GetCurrentTimeAsync` | HttpClient, Polly, Caching |
| `ExternalServices/Time/WorldTimeApiResponse.cs` | Time API DTO + tolerant parse | `ParsedDateTime` | BCL JSON |
| `ExternalServices/Time/TimeApiSettings.cs` | Bound `TimeApi` config | settings props | MS.Options |
| `ExternalServices/Time/TimeServicePollyExtensions.cs` | Retry/breaker/timeout policies | `AddTimeServiceWithPolly` | Polly, Http |
| `ExternalServices/Time/TimeServiceHealthCheck.cs` | Health check over `ITimeService` | `CheckHealthAsync` | HealthChecks abstractions |
| `BackgroundJobs/OpenSessionTimeoutJob.cs` | Hourly auto-timeout sweep | `RunOnceAsync` | Hosting, Application |
| `DependencyInjection.cs` | `AddInfrastructure` (DbContext, repos, time, job) | `AddInfrastructure` | EF Core, DI |

### Backend — `AttendanceSystem.Api`
| File | Responsibility | Key API | Depends on |
|------|----------------|---------|-----------|
| `Program.cs` | Composition root + pipeline + migrate/seed | top-level program | all layers |
| `Controllers/AuthController.cs` | login/refresh/logout | `Login`, `Refresh`, `Logout` | UoW, TokenService, BCrypt |
| `Controllers/AttendanceController.cs` | clock-in/out/status/history/active/correct | actions | MediatR (ISender) |
| `Controllers/EmployeeController.cs`, `HealthController.cs` | placeholder controllers | — | — |
| `Middleware/GlobalExceptionMiddleware.cs` | Exception → RFC 7807 ProblemDetails | `InvokeAsync` | Domain exceptions, EF, SqlClient |
| `Middleware/RequestLoggingMiddleware.cs` | (placeholder; Serilog request logging used) | — | — |
| `Services/TokenService.cs` | JWT + refresh token generation/hash | `GenerateAccessToken`, `GenerateRefreshToken`, `HashRefreshToken` | IdentityModel, crypto |
| `Configuration/JwtSettings.cs` | Bound `Jwt` config | settings props | MS.Options |
| `Contracts/AuthContracts.cs`, `AttendanceContracts.cs` | Request/response DTOs | records | BCL |
| `Extensions/ServiceCollectionExtensions.cs` | JWT, CORS, rate limiting, Swagger | `AddJwtAuthentication`, `AddRateLimiting`, `AddCorsForFrontend`, `AddSwaggerWithJwt` | ASP.NET |
| `Extensions/HealthCheckExtensions.cs` | Register health checks | `AddHealthChecksConfiguration` | HealthChecks |
| `Extensions/ApplicationBuilderExtensions.cs` | (placeholder) | — | — |

### Frontend — `attendance-frontend/src`
| File | Responsibility | Key exports | Depends on |
|------|----------------|-------------|-----------|
| `main.tsx` | React entry; mounts `<App/>` | — | React DOM |
| `App.tsx` | Providers (QueryClient, Router) + routes | `App` | React Query, Router |
| `api/client.ts` | Axios instance + JWT + refresh queue | default `client` | axios, authStore |
| `api/authApi.ts` | Auth endpoints | `authApi` | client |
| `api/attendanceApi.ts` | Attendance endpoints | `attendanceApi` | client, types |
| `store/authStore.ts` | Zustand auth state (in-memory tokens) | `useAuthStore` | zustand, authApi |
| `store/attendanceStore.ts` | Toast notifications store | `useToastStore` | zustand |
| `hooks/useAttendance.ts` | Status query + clock mutations | `useAttendance` | React Query |
| `hooks/useAuth.ts` | Auth store selectors | `useAuth` | authStore |
| `hooks/useCurrentTime.ts` | 1s ticking clock anchored to backend time | `useCurrentTime` | React |
| `hooks/useNetworkStatus.ts` | online/offline state | `useNetworkStatus` | React |
| `components/ClockButton/ClockButton.tsx` | The clock-in/out control + live clock | `ClockButton` | hooks, dateUtils |
| `components/ClockButton/ConfirmDialog.tsx` | Confirm modal (Esc/Enter) | `ConfirmDialog` | React |
| `components/AttendanceHistory/*` | History table, rows, filters, CSV | `AttendanceHistory`, `HistoryRow` | React Query |
| `components/AdminDashboard/*` | Active employees + force clock-out | `AdminDashboard`, `ActiveEmployeesList` | React Query |
| `components/common/*` | Spinner, ErrorMessage, StatusBadge, NetworkStatus, Toaster, AppHeader, ProtectedRoute | each component | React, Router |
| `pages/LoginPage.tsx`, `EmployeePage.tsx`, `AdminPage.tsx` | Route pages | each page | components |
| `types/auth.ts`, `types/attendance.ts` | TypeScript DTO types | interfaces | — |
| `utils/dateUtils.ts` | Zurich formatting, elapsed, CSV | `formatZurich*`, `formatElapsed`, `recordsToCsv` | date-fns/Intl |
| `utils/errorUtils.ts` | Axios error → message + status | `getErrorMessage`, `getStatusCode` | axios |

### Tests & config
| File | Responsibility |
|------|----------------|
| `UnitTests/Domain/AttendanceDomainTests.cs` | Domain invariants |
| `UnitTests/ClockInHandlerTests.cs` | Clock-in/out/correction handlers (mocked) |
| `UnitTests/TimeServiceTests.cs` | Cached time service + circuit breaker |
| `UnitTests/PipelineBehaviorTests.cs` | Validation pipeline aggregates errors |
| `IntegrationTests/AttendanceFlowTests.cs` | 10 end-to-end flows over real SQL |
| `IntegrationTests/IntegrationApiFactory.cs` | `WebApplicationFactory` + isolated DB + mock time |
| `IntegrationTests/SqlServerFactAttribute.cs` | Skip integration tests if no SQL reachable |
| `docker-compose.yml` | SQL Server 2022 container |
| `appsettings.json` / `appsettings.Development.json` | Configuration (Section 11) |

---

# SECTION 11: Configuration Reference

Configuration is layered: `appsettings.json` (base, all environments) is overlaid by
`appsettings.{Environment}.json` (e.g. `appsettings.Development.json`), then environment variables,
then User Secrets (Development) / a secret store (Production). Later sources win.

## 11.1 `appsettings.json` (base)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=AttendanceDB;User Id=sa;Password=Attendance@Strong123!;TrustServerCertificate=True;"
  },
  "TimeApi": {
    "BaseUrl": "https://timeapi.io/api",
    "Timezone": "Europe/Zurich",
    "TimeoutSeconds": 3,
    "RetryCount": 2,
    "CircuitBreakerThreshold": 3,
    "CircuitBreakerDurationSeconds": 30,
    "CacheTtlSeconds": 5
  },
  "Jwt": {
    "Secret": "REPLACE_WITH_32_CHAR_SECRET_IN_USER_SECRETS",
    "Issuer": "AttendanceSystem",
    "Audience": "AttendanceSystemClients",
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 7
  },
  "AttendanceRules": {
    "AutoTimeoutHours": 16,
    "MaxShiftHours": 12,
    "MinBreakBetweenShiftsHours": 8
  },
  "Serilog": { "MinimumLevel": { "Default": "Information", "Override": { "Microsoft": "Warning", "System": "Warning" } },
    "WriteTo": [ { "Name": "Console" }, { "Name": "File", "Args": { "path": "logs/attendance-.txt", "rollingInterval": "Day" } } ] }
}
```

| Key | Controls | If wrong | Safe default |
|-----|----------|----------|--------------|
| `ConnectionStrings:DefaultConnection` | Which SQL Server/DB EF Core connects to. | App can't migrate/seed and every DB-backed endpoint fails (the startup block logs the error and the app still boots). | The Docker compose value `Server=localhost,1433;…` |
| `TimeApi:BaseUrl` | Base URL of the external time provider. | Time fetch fails → clock-in/out return `503`; the path is appended as `/Time/current/zone?timeZone=…`, so a wrong base breaks the only time source. | `https://timeapi.io/api` (reachable via HTTPS) |
| `TimeApi:Timezone` | Which zone to request and render. | Wrong zone → wrong wall-clock times recorded/displayed. | `Europe/Zurich` |
| `TimeApi:TimeoutSeconds` | Polly per-request timeout. | Too low → spurious timeouts; too high → users wait on a stuck call. | `3` |
| `TimeApi:RetryCount` | Polly retry attempts (exponential backoff). | `0` removes resilience to blips; very high adds load/latency. | `2` |
| `TimeApi:CircuitBreakerThreshold` | Consecutive failures before the breaker opens. | Too low → breaker trips on noise; too high → slow calls pile up during an outage. | `3` |
| `TimeApi:CircuitBreakerDurationSeconds` | How long the breaker stays open. | Too short → hammers a recovering API; too long → needless downtime. | `30` (matches `Retry-After`) |
| `TimeApi:CacheTtlSeconds` | In-memory time cache lifetime. | `0` disables burst coalescing; large → stale recorded times. | `5` |
| `Jwt:Secret` | HMAC-SHA256 signing key for JWTs. | Too short/weak → forgeable tokens; **must** be ≥ 32 chars and kept secret. | none in source — see User Secrets |
| `Jwt:Issuer` / `Jwt:Audience` | Token issuer/audience validated on every request. | Mismatch between issuing and validating config → all tokens rejected (`401`). | `AttendanceSystem` / `AttendanceSystemClients` |
| `Jwt:AccessTokenExpiryMinutes` | Access token lifetime. | Too long → larger theft window; too short → frequent refresh. | `15` |
| `Jwt:RefreshTokenExpiryDays` | Refresh token lifetime. | Too long → long-lived theft risk; too short → frequent re-login. | `7` |
| `AttendanceRules:AutoTimeoutHours` | Auto-close threshold for the background job. | Too low → closes real long shifts; too high → forgotten sessions linger. | `16` |
| `AttendanceRules:MaxShiftHours` / `MinBreakBetweenShiftsHours` | Reserved business limits (config present for future rules). | n/a today | `12` / `8` |
| `Serilog:*` | Log levels and sinks (console + daily rolling file). | Too verbose → noise; too quiet → blind. | `Information`, `Microsoft`/`System` → `Warning` |

## 11.2 `appsettings.Development.json` (overlay)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=AttendanceDB;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "UseTimeMock": false,
  "Logging": { "LogLevel": { "Default": "Debug" } }
}
```

| Key | Controls | Notes |
|-----|----------|-------|
| `ConnectionStrings:DefaultConnection` | Dev DB override. | Points at **LocalDB** (`(localdb)\MSSQLLocalDB`) with Windows auth, so the app runs without Docker. Overrides the base connection in Development only. |
| `UseTimeMock` | Selects the time-service implementation in `AddInfrastructure`. | `false` → the real `CachedTimeService`/`WorldTimeApiClient` is used (live Zurich time). `true` → a deterministic `MockTimeService` (handy for tests/offline demos, but it does **not** provide real Zurich time). |
| `MockTime` (optional) | Seed instant for the mock. | Only read when `UseTimeMock=true`; absent here. |
| `Logging:LogLevel:Default` | Built-in logging level (Debug in dev). | More verbose locally; Serilog still governs sink behavior. |

> The integration tests don't rely on this file: their `IntegrationApiFactory` runs under the
> `Testing` environment and overrides the DbContext (an isolated per-run database) and the time
> service (mock) directly via `ConfigureTestServices`, so tests are deterministic regardless of
> these settings.

## 11.3 User Secrets — why the JWT secret and DB password are not in `appsettings.json`

`appsettings.json` is committed to source control. **Secrets must never be committed**: a leaked
repo would expose the JWT signing key (letting anyone forge admin tokens) and the database password.

In Development, .NET provides **User Secrets** — a JSON file stored *outside* the repo in your user
profile, loaded automatically when the environment is Development. You set them with:

```bash
dotnet user-secrets set "Jwt:Secret" "<a real 32+ character random secret>" --project AttendanceSystem.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your dev connection>" --project AttendanceSystem.Api
```

Because configuration sources are layered, the User Secrets value **overrides** the placeholder in
`appsettings.json` without changing the committed file. The placeholder
`REPLACE_WITH_32_CHAR_SECRET_IN_USER_SECRETS` is deliberately obvious — it is *not* a real secret;
it's a reminder to set one. In Production, the equivalent values come from environment variables or a
managed secret store (Azure Key Vault, AWS Secrets Manager, Kubernetes secrets), never from a file in
the image.

---

# SECTION 12: Security Model

A defense-in-depth summary of how the system protects identity, data, and availability.

## 12.1 JWT authentication

A **JSON Web Token** is a signed, self-describing credential. After login, the server issues a JWT
containing claims (employee id, name, email, role, badge, `jti`, `exp`) and signs it with HMAC-SHA256
using `Jwt:Secret`. The client sends it on every request as `Authorization: Bearer <token>`. On each
request, `UseAuthentication` **validates**:

- **Signature** — recomputes the HMAC with the secret; if it doesn't match, the token was tampered
  with → `401`. (This is why the secret must stay secret: anyone with it can mint valid tokens.)
- **Issuer / Audience** — must equal the configured `Issuer`/`Audience`.
- **Lifetime** — `exp` must be in the future, with `ClockSkew = 0` (no grace period).

If valid, the claims populate `HttpContext.User`, and controllers read identity/role from there. JWTs
are **stateless**: the server doesn't store sessions; everything needed is in the (signed) token.

## 12.2 Refresh token rotation — preventing replay

Refresh tokens are 64 bytes of CSPRNG randomness, stored **hashed** (SHA-256) on the employee, with a
7-day expiry. On every `/refresh`, a new token is issued and the stored hash replaced, so the old
token is immediately void. If a refresh token is stolen and used, the legitimate user's next refresh
(or the thief's) invalidates the other, breaking the victim's session and surfacing the compromise —
rather than letting a stolen token be replayed silently for its full 7-day life. `logout` clears the
stored hash, killing the refresh capability.

## 12.3 BCrypt password hashing

Passwords are stored only as BCrypt hashes (`nvarchar(512)`). BCrypt is adaptive (tunable **work
factor** / cost) and salts each hash automatically. The work factor is the log2 number of hashing
rounds; raising it makes each hash exponentially slower, keeping brute-force expensive as hardware
improves. Login uses `BCrypt.Verify(plain, storedHash)`; the plain password is never stored or
logged. Salting defeats rainbow tables; slowness defeats mass guessing.

## 12.4 Rate limiting

Clock-in and clock-out carry `[EnableRateLimiting("clock-operations")]` — a fixed window of **5
requests per minute, partitioned by authenticated user id** (falling back to client IP). This blunts
accidental double-submits, malicious spam, and brute-force-style hammering of the time-dependent
endpoints. The 6th request in a window receives `429 Too Many Requests`.

## 12.5 Tokens in memory (not `localStorage`) — XSS mitigation

The frontend keeps tokens only in Zustand (JavaScript memory), never in `localStorage`/`sessionStorage`.
If an XSS flaw let an attacker run script on the page, `localStorage`-resident tokens could be read
and exfiltrated for a persistent account takeover. In-memory tokens are not in a known, scriptable
storage key and vanish on reload — shrinking both the likelihood and the lifetime of a token theft.
(See Section 7.2.)

## 12.6 SQL injection — why EF Core is immune here

All data access goes through EF Core LINQ (e.g. `Where(e => e.Email == normalized)`), which EF
translates into **parameterized SQL**: user values are sent as bound parameters (`@p0`), never
concatenated into the SQL text. So a malicious email like `'; DROP TABLE Employees;--` is treated as a
literal string to compare, not as SQL to execute. There is no hand-built SQL string anywhere in the
data layer, so the classic injection vector simply does not exist.

## 12.7 Role-based authorization

Three roles, enforced server-side via `[Authorize(Roles = ...)]` and explicit checks:

| Capability | Employee | Manager | Admin |
|------------|:--------:|:-------:|:-----:|
| Clock in / out (self) | ✅ | ✅ | ✅ |
| View own status / history | ✅ | ✅ | ✅ |
| View **another** employee's history (`?employeeId=`) | ❌ (`403`) | ✅ | ✅ |
| View all active sessions (`GET /active`) | ❌ (`403`) | ✅ | ✅ |
| Force clock-out another employee | ❌ | ✅ | ✅ |
| Manually correct a log (`PUT /{id}/correct`) | ❌ | ❌ | ✅ |

The frontend `ProtectedRoute` mirrors this for UX (hiding pages), but the **authoritative** check is
on the server — a crafted request from an Employee for someone else's data still returns `403`.

## 12.8 The `Retry-After` header on 503

When the time service is unavailable, the `503` response carries `Retry-After: 30`. This tells
well-behaved clients and proxies *when* to retry (after the 30-second circuit-breaker window),
preventing a thundering herd of immediate retries that would keep the circuit open. It turns an
opaque failure into cooperative, responsible backoff.

---

# SECTION 13: Edge Cases & How Each Is Handled

For each: the scenario, what the system does, where it's handled, and why.

### 1. Employee clicks Clock In twice rapidly (double-submit)
**What happens:** The first request creates the open session; the second finds an existing open
session and is rejected with `409 Conflict`.
**Where:** `ClockInCommandHandler` step 2 (`GetActiveSessionAsync` → `AlreadyClockedInException`),
plus rate limiting smooths rapid clicks. (True *simultaneous* requests → case 13.)
**Why:** Idempotency — one open session per employee is a core invariant.

### 2. Employee forgets to Clock Out (session open 16+ hours)
**What happens:** The hourly `OpenSessionTimeoutJob` finds sessions older than
`AutoTimeoutHours` (16), calls `ApplyAutoTimeout` (sets clock-out, `IsAutoTimeout = true`), and writes
an `AuditLog` with action `AutoTimeout`.
**Where:** `OpenSessionTimeoutJob.RunOnceAsync` + `AttendanceLog.ApplyAutoTimeout`.
**Why:** Open sessions can't linger forever; the flag tells managers the end time is an estimate.

### 3. Shift crosses midnight (in Jan 14 23:55, out Jan 15 00:10)
**What happens:** Stored as two offset-bearing instants; duration is computed by subtraction
(correct = 15 min). The history DTO sets `CrossesMidnight = true` and formats
`"0h 15m (crosses midnight)"`.
**Where:** `DurationFormatter.CrossesMidnight`/`FormatDuration` (converts both ends to the
Europe/Zurich calendar date and compares), surfaced by `GetAttendanceHistoryQueryHandler`.
**Why:** Duration must be a pure elapsed-time subtraction (unaffected by the calendar boundary), and
the UI flags the crossing for clarity.

### 4. External time API completely down
**What happens:** `WorldTimeApiClient` throws `TimeServiceUnavailableException`; the handler
re-throws; middleware returns `503` + `Retry-After: 30`. **No record is written.**
**Where:** `WorldTimeApiClient` (catch/translate) → handler (re-throw) → `GlobalExceptionMiddleware`.
**Why:** Never record an unverified time; fail loudly instead. (Full trace: Section 8, Flow C.)

### 5. External time API responds slowly (> 3 seconds)
**What happens:** Polly's 3-second pessimistic timeout abandons the call with
`TimeoutRejectedException`; after `RetryCount` attempts it surfaces as
`TimeServiceUnavailableException` → `503`.
**Where:** `TimeServicePollyExtensions` (timeout policy) + `WorldTimeApiClient`.
**Why:** Clock-in is interactive — fail fast rather than make the user wait on a stuck dependency.

### 6. External time API returns malformed JSON
**What happens:** `JsonSerializer.Deserialize`/`ParsedDateTime` throws `JsonException`/`FormatException`;
`WorldTimeApiClient` catches it and throws `TimeServiceUnavailableException("Invalid response format…")`
→ `503`.
**Where:** `WorldTimeApiClient` catch blocks; `WorldTimeApiResponse.ParsedDateTime`.
**Why:** A garbage time is as dangerous as no time — refuse it.

### 7. Circuit breaker is open — request arrives
**What happens:** Polly throws `BrokenCircuitException` *immediately* (no HTTP call). The client
wraps it as `TimeServiceUnavailableException` → `503`.
**Where:** Polly circuit-breaker policy; `WorldTimeApiClient` catch.
**Why:** During an outage, fail instantly and give the dependency 30s to recover instead of piling
on slow calls.

### 8. Employee is deactivated mid-shift
**What happens:** They can no longer authenticate/clock in/out (`IsActive` check → treated as
`EmployeeNotFoundException`). Their already-open session is still closed by the auto-timeout job,
because `GetOpenSessionsOlderThanAsync` deliberately does **not** filter on `IsActive`.
**Where:** Handlers' active-employee check; `AttendanceRepository.GetOpenSessionsOlderThanAsync`
(intentionally ignores the global filter for open sessions); the global query filter elsewhere.
**Why:** A deactivated employee shouldn't act, but their dangling session must still be reconciled.

### 9. Admin manually corrects a clock-in/out time
**What happens:** `PUT /{logId}/correct` (Admin only) → `ManualCorrectionCommand`. The handler
captures the old values, calls `ApplyManualCorrection` (validates the *effective* result so
clock-out > clock-in), sets `IsManualCorrection = true`, records `CorrectedByEmployeeId` +
`CorrectionNotes`, and writes an `AuditLog` with old + new JSON.
**Where:** `ManualCorrectionCommandHandler`, `AttendanceLog.ApplyManualCorrection`.
**Why:** Edits to a compliance record must be authorized, validated, and fully audited.

### 10. Network drops mid-request on the frontend
**What happens:** The Axios call rejects with no response; `getErrorMessage` returns "No connection.
Please check your internet." The `useNetworkStatus` banner shows and the ClockButton is disabled
while `navigator.onLine` is false.
**Where:** `errorUtils.getErrorMessage`, `useNetworkStatus`, `NetworkStatus`, `ClockButton` disabled
state.
**Why:** Honest, immediate feedback; don't let the user attempt operations that can't reach the
server.

### 11. JWT expires while the user is logged in
**What happens:** The next request returns `401`; the Axios response interceptor transparently calls
`/refresh`, gets a new access token, and retries the original request — invisible to the user. If
refresh fails, it logs out and redirects to `/login`.
**Where:** `client.ts` response interceptor + the failed-queue pattern; `authStore.refreshAccessToken`.
**Why:** Short access tokens for security without forcing constant manual re-login.

### 12. Refresh token is stolen and reused
**What happens:** Rotation means whoever refreshes first invalidates the other's token; the victim's
next request fails and the session breaks, exposing the compromise instead of allowing indefinite
replay.
**Where:** `AuthController.Refresh` (issues new token + overwrites stored hash); `TokenService`.
**Why:** Limit the value/lifetime of a stolen refresh token.

### 13. Two simultaneous clock-in requests for the same employee (race condition)
**What happens:** Both may pass the in-handler `GetActiveSessionAsync` check at the same instant, but
only one `INSERT` can satisfy the unique filtered index `UX_OneActiveSession`; the other fails with a
SQL unique-violation, which the middleware maps to `409 Conflict`.
**Where:** DB index `UX_OneActiveSession`; `GlobalExceptionMiddleware` (`DbUpdateException` + SQL
error 2601/2627 → 409).
**Why:** The application check is racy under true concurrency; the database constraint is the
authoritative serialization point.

### 14. DST change during a shift (Zurich +01:00 → +02:00)
**What happens:** Each instant is stored with the offset that was in effect at *that* moment.
Subtraction yields the correct real elapsed time across the transition (e.g. a shift spanning the
spring-forward is one hour shorter in wall-clock terms but correct in elapsed terms). Display uses
`TimeZoneInfo`/`Intl` for the Europe/Zurich zone, which is DST-aware.
**Where:** `DateTimeOffset` storage (`datetimeoffset(7)`), `DurationFormatter`/`WorldTimeApiResponse`
zone resolution, frontend `Intl.DateTimeFormat`.
**Why:** Offset-bearing instants make DST a non-issue for math; zone-aware formatting makes it
correct for humans.

---

# SECTION 14: Running the Project — Full Setup Guide

For a developer seeing this repository for the first time.

### 1. Prerequisites
- **.NET 8 SDK** (the API targets `net8.0`). If you only have the .NET 9 SDK, you can still build,
  but running needs the .NET 8 runtime or a roll-forward flag (see step 6).
- **Node.js 18+** (Node 22 verified) for the frontend.
- **A SQL Server**, via either:
  - **Docker** (runs SQL Server 2022 from `docker-compose.yml`), or
  - **SQL Server LocalDB** (ships with Visual Studio / SQL Server Express) for a Docker-free run.
- An editor: **VS Code** or **JetBrains Rider**.

### 2. Get the code
```bash
# from your workspace
cd AttendanceSystem
```

### 3a. Start the database with Docker (production-like)
```bash
docker-compose up -d        # SQL Server 2022 on localhost,1433
docker ps                   # confirm attendance_sqlserver is healthy
```
This uses the connection string in `appsettings.json` (`Server=localhost,1433; … User Id=sa …`).

### 3b. …or use LocalDB (no Docker)
`appsettings.Development.json` already points `DefaultConnection` at `(localdb)\MSSQLLocalDB`, so in
Development you need nothing extra — the app will use LocalDB. Start it once if needed:
```bash
sqllocaldb start MSSQLLocalDB
```

### 4. Set User Secrets (recommended for the JWT secret)
```bash
dotnet user-secrets set "Jwt:Secret" "a-real-random-secret-at-least-32-characters" --project AttendanceSystem.Api
# (optional) override the dev DB connection or SA password the same way
```

### 5. Migrations
The API applies pending migrations automatically on startup (`await db.Database.MigrateAsync()`), so
normally you do nothing. To apply them manually:
```bash
dotnet ef database update --project AttendanceSystem.Infrastructure --startup-project AttendanceSystem.Api
```

### 6. Run the API
```bash
dotnet run --project AttendanceSystem.Api
# If you only have the .NET 9 SDK (no .NET 8 runtime), roll forward:
DOTNET_ROLL_FORWARD=Major dotnet run --project AttendanceSystem.Api --no-launch-profile
```
On startup it migrates + seeds the DB and logs "Database migrated and seeded successfully."
Swagger UI is at `/swagger` in Development.

### 7. Run the frontend
```bash
cd attendance-frontend
npm install
npm run dev     # Vite dev server, http://localhost:5173 (or 5174 if 5173 is busy)
```
Set `attendance-frontend/.env` → `VITE_API_URL` to the API base URL (the CORS policy allows
`localhost:5173`, `5174`, and `3000`).

### 8. Seed credentials & first login
Password for all three seed users is `Test@1234`:
| Email | Role |
|-------|------|
| `anna@company.ch` | Employee |
| `hans@company.ch` | Manager |
| `admin@company.ch` | Admin |
Log in as Anna to clock in/out; log in as Admin to see the dashboard and force clock-out.

### 9. Run unit tests
```bash
dotnet test AttendanceSystem.UnitTests
```
Fast, no I/O — Domain invariants, handlers (mocked), the cached time service/circuit breaker, and the
validation pipeline.

### 10. Run integration tests
```bash
# Needs a reachable SQL Server (LocalDB by default). They skip automatically if none is found.
dotnet test AttendanceSystem.IntegrationTests
# In CI, point them at a Testcontainers MsSql instance:
#   ATTENDANCE_TEST_CONNECTION="Server=...;Database=master;User Id=sa;Password=...;TrustServerCertificate=True"
```
These boot the real API in-process against an isolated database and exercise all ten end-to-end
flows (login, clock-in, double clock-in 409, clock-out, time-down 503, midnight crossing,
auto-timeout job, manual correction, rate limiting, role authorization).

---

# APPENDIX A: Glossary of Terms

A quick reference for the concepts and acronyms used throughout this document.

| Term | Meaning in this codebase |
|------|--------------------------|
| **Aggregate / Entity** | A domain object with a persistent identity (`Id`). `Employee` and `AttendanceLog` are entities. |
| **Value Object** | An immutable object defined entirely by its values, compared by value, with no identity. `ZurichTime`. |
| **Clean Architecture** | A layering where source dependencies point inward toward the dependency-free Domain. |
| **Dependency Rule** | "Source code dependencies only point inward." The Domain depends on nothing. |
| **Dependency Inversion** | The Application declares interfaces (`ITimeService`, `IUnitOfWork`); Infrastructure implements them. |
| **CQRS** | Command/Query Responsibility Segregation — separate request types for writes (Commands) and reads (Queries). |
| **MediatR** | The in-process dispatcher that routes a Command/Query to its single Handler, through a pipeline. |
| **Pipeline Behavior** | MediatR "middleware" wrapping every request — here, validation and logging. |
| **Repository** | An abstraction exposing domain-meaningful data operations (`GetActiveSessionAsync`). |
| **Unit of Work** | Groups repositories and commits all pending changes in one transaction via `SaveChangesAsync`. |
| **DTO** | Data Transfer Object — a flat, serializable shape returned to clients (e.g. `ClockInResult`). |
| **EF Core** | Entity Framework Core — the ORM mapping C# objects to SQL tables; generates parameterized SQL. |
| **Fluent API** | EF Core's code-based mapping configuration (`IEntityTypeConfiguration<T>`), keeping the Domain clean. |
| **Migration** | A versioned, code-generated schema change applied with `dotnet ef database update`. |
| **Filtered index** | An index over only the rows matching a predicate (`WHERE ClockOutUtc IS NULL`). |
| **`DATETIMEOFFSET(7)`** | SQL Server type storing an instant **plus** its UTC offset, at 100 ns precision. |
| **`DateTimeOffset`** | The .NET counterpart — an unambiguous, DST-safe instant carrying its offset. |
| **Decorator Pattern** | Wrapping an implementation with another of the same interface to add behavior (`CachedTimeService`). |
| **Circuit Breaker** | A Polly policy with Closed/Open/Half-Open states that fails fast during a dependency outage. |
| **Exponential backoff** | Retrying with doubling delays (0.5s, 1.0s) to avoid hammering a struggling service. |
| **Idempotency** | An operation that, repeated, doesn't create duplicate effects — "one open session per employee." |
| **JWT** | JSON Web Token — a signed, stateless bearer credential carrying claims. |
| **Claim** | A key/value assertion inside a JWT (e.g. `role = Admin`, `sub = <employee id>`). |
| **`ClockSkew`** | Allowed tolerance on token expiry validation; set to `0` here for exact expiry. |
| **Refresh token rotation** | Issuing a new refresh token (and invalidating the old) on every refresh, to limit replay. |
| **BCrypt** | An adaptive, salted password hashing function with a tunable work factor. |
| **CSPRNG** | Cryptographically Secure Pseudo-Random Number Generator — used for refresh tokens. |
| **RFC 7807 ProblemDetails** | The standard JSON error envelope (`type`, `title`, `status`, `detail`, `instance`). |
| **Rate limiting** | Capping requests per window per user (5/min on clock operations) → `429` beyond the cap. |
| **CORS** | Cross-Origin Resource Sharing — the browser policy allowing the SPA's origin to call the API. |
| **`BackgroundService`** | An ASP.NET Core hosted service running a long-lived loop (the auto-timeout sweep). |
| **`IServiceScopeFactory`** | Creates DI scopes so a singleton background service can resolve scoped services safely. |
| **Soft delete** | Marking a row inactive (`IsActive = false`) instead of physically deleting it. |
| **React Query** | Frontend library managing *server* state: caching, refetch intervals, cache invalidation. |
| **Zustand** | Tiny frontend store for *client* state (the in-memory auth tokens). |
| **XSS** | Cross-Site Scripting — the attack class that in-memory token storage mitigates. |
| **LocalDB** | A lightweight, on-demand SQL Server engine for local development (no Docker needed). |
| **Testcontainers** | A library that spins up a real SQL Server in a Docker container for integration tests. |

---

*End of PROJECT_EXPLAINED.md.*











