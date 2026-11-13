# School Admission System

A first-come-first-served school admission system. An administrator creates independently seat-limited
classes, a parent identifies a student with a 17-digit birth registration number, reserves a seat that
is held for a configurable time, and then confirms a simulated payment or cancels. Seat counts are
derived from hold rows on every read, so an expired hold releases its seat with no background worker,
and a per-class PostgreSQL row lock keeps the system from overselling under concurrent requests.

Stack: .NET 8 (ASP.NET Core controllers, EF Core, Npgsql), PostgreSQL 17, React 18 with TypeScript and
Vite, TanStack Query, Tailwind CSS, Docker Compose.

## Requirements

Docker only. You do not need .NET, Node, npm or PostgreSQL installed on the host — the Compose file
builds the API and the frontend from source and runs PostgreSQL in a container.

- Docker Engine 24+ with the Compose v2 plugin (`docker compose version`).

## Quick start

```bash
cp .env.example .env
docker compose up --build
```

Then open:

| What | URL |
|---|---|
| Frontend UI | http://localhost:5173 |
| API | http://localhost:8080 |
| API health check | http://localhost:8080/health |

The first build takes a few minutes. The API waits for the database health check before it starts and
applies the EF Core migrations itself (`APPLY_MIGRATIONS=true`), so there is no separate migration step.

## Shutdown

```bash
docker compose down
```

This stops and removes the containers but keeps the named volume `pgdata`, so the data is still there
on the next `docker compose up`.

> **Warning:** `docker compose down -v` also deletes the `pgdata` volume. Every class and every
> application is lost. Do not use `-v` unless you want an empty database.

## Environment variables

Copy `.env.example` to `.env` and change what you need. Compose reads `.env` automatically.

| Variable | Default | Used by | What it does |
|---|---|---|---|
| `HOLD_DURATION_SECONDS` | `600` | API | How long a new hold stays active, in seconds. Read once at startup; a value that is not a positive integer aborts startup with a logged error. Changing it needs a container restart. |
| `POSTGRES_USER` | `postgres` | db, API | Database user created in the PostgreSQL container and used in the API connection string. |
| `POSTGRES_PASSWORD` | `postgres` | db, API | Database password. The default is a local demo value, not a secret; set your own for anything reachable. |
| `POSTGRES_DB` | `school_admission` | db, API | Database name created on first start. |
| `ConnectionStrings__DefaultConnection` | `Host=db;Port=5432;Username=…;Password=…;Database=…` | API | Full Npgsql connection string. Compose builds it from the three `POSTGRES_*` values; set it directly to point the API at another database. |
| `ASPNETCORE_URLS` | `http://0.0.0.0:8080` | API | Address the API binds to inside its container. Compose publishes it on host port 8080. |
| `VITE_API_BASE_URL` | `http://localhost:8080` (from `frontend/.env.production`) | frontend build | Base URL the browser uses for API calls. Vite inlines `VITE_*` variables at **build** time, so changing it requires `docker compose up --build`, not just a restart. The container build runs `vite build` in production mode, which loads `frontend/.env.production` and inlines `http://localhost:8080`; the browser then calls the API directly and the API's `FRONTEND_ORIGIN` CORS allowance covers it. |
| `FRONTEND_ORIGIN` | `http://localhost:5173` | API | The single origin allowed by the API's CORS policy. |

## API

Base URL `http://localhost:8080`. Requests and responses are JSON; errors are
`application/problem+json` with `type`, `title`, `status` and `detail`. Timestamps are ISO 8601 UTC
with a `Z` suffix. Student IDs are exactly 17 ASCII digits and travel in the `X-Student-Id` header.

| # | Method | Path | Required header | Success | Response fields |
|---|---|---|---|---|---|
| 1 | POST | `/classes` | — | `201` | `id`, `name`, `totalSeats` |
| 2 | GET | `/classes` | — | `200` | array of `id`, `name`, `totalSeats`, `heldSeats`, `confirmedSeats`, `availableSeats` |
| 3 | GET | `/classes/{classId}` | — | `200` | `id`, `name`, `totalSeats`, `heldSeats`, `confirmedSeats`, `availableSeats` |
| 4 | POST | `/classes/{classId}/holds` | `X-Student-Id` | `201` new, `200` duplicate | `holdId`, `classId`, `studentId`, `status`, `expiresAt` |
| 5 | GET | `/holds/{holdId}` | — | `200` | `holdId`, `classId`, `status`, `expiresAt` |
| 6 | POST | `/holds/{holdId}/confirm` | `X-Student-Id` | `200` | `holdId`, `status` |
| 7 | DELETE | `/holds/{holdId}` | `X-Student-Id` | `204` (no body) | — |
| 8 | GET | `/me/holds` | `X-Student-Id` | `200` | array of `holdId`, `classId`, `className`, `status`, `expiresAt` |

`status` is one of `HELD`, `CONFIRMED`, `EXPIRED`, `CANCELLED`. Only `HELD`, `CONFIRMED` and
`CANCELLED` are stored; `EXPIRED` is derived from `status = 'HELD' AND expires_at <= database now`.

Example:

```bash
curl -s -X POST http://localhost:8080/classes \
  -H 'Content-Type: application/json' \
  -d '{"name":"Class 6 - Morning Shift","totalSeats":120}'

curl -s -X POST http://localhost:8080/classes/c_123/holds \
  -H 'X-Student-Id: 20152691234567890'
```

Error codes are listed in `ASSUMPTIONS.md` and in PRD section 6.2.

## Backend tests

The tests are xUnit integration tests that run the real HTTP stack against a **real PostgreSQL
database**. There is no in-memory provider and no fallback connection string: the test host reads
`TEST_DATABASE_URL` and fails loudly when it is missing.

1. Start a PostgreSQL 17 instance you can reach from the host. The simplest way is the Compose
   database, published on port 5433:

   ```bash
   docker run -d --name admission-test-pg -p 5433:5432 \
     -e POSTGRES_PASSWORD=postgres postgres:17
   ```

2. Create the test database:

   ```bash
   PGPASSWORD=postgres psql -h 127.0.0.1 -p 5433 -U postgres -c 'CREATE DATABASE sa_test'
   ```

3. Run the suite. The test host drops and recreates the schema from the real migrations at startup,
   so the database must be dedicated to the tests.

   ```bash
   TEST_DATABASE_URL='Host=127.0.0.1;Port=5433;Username=postgres;Password=postgres;Database=sa_test' \
     dotnet test backend.Tests/SchoolAdmission.Tests.csproj \
       -m:1 -p:UseSharedCompilation=false -nodeReuse:false -- xUnit.MaxParallelThreads=1
   ```

   The single-threaded flags keep the run reproducible on a small machine; the suite is safe to run
   without them, and once the concurrency test lands it is easier to read when it is not competing
   for CPU.

On this branch the documented command runs the 18 foundation tests only. Once the classes and holds
branches merge, the same suite also covers the class endpoints and the hold lifecycle (create,
duplicate, confirm, cancel, expiry), ownership and idempotency.

**Required but pending:** a concurrency test that fires 50 simultaneous distinct-student hold requests
at a 10-seat class and asserts `heldSeats + confirmedSeats <= totalSeats` afterwards. That test is a
Must-prove item and is written by the concurrency task; it is not in the tree yet, so the command above
does not run it today. The README is updated when that branch lands.

## Frontend tests and type check

```bash
cd frontend
npm ci
npm run typecheck   # tsc --noEmit, strict mode
npm test -- --run   # vitest
npm run build       # production build
```

The frontend tests cover the invalid-student-ID validation, the `HELD` → `EXPIRED` UI transition, and
the `409` no-seats message. They do not assume the browser clock decides reservation eligibility; the
server's `expiresAt` is authoritative.

## Architecture

- **Controllers** (`backend/Controllers/`) map HTTP to service calls and map service results to status
  codes. They hold no business logic.
- **Services** (`backend/Services/`) own the transactions, the lock protocol and the state transitions.
- **Per-class row lock.** Every path that can change occupancy — hold create, confirm, cancel — opens a
  transaction, runs `SELECT * FROM classes WHERE id = @id FOR UPDATE`, and only then reads state. The
  lock serialises all capacity decisions for one class, which is what makes overselling impossible.
- **Database time, read after the lock.** The decision timestamp comes from PostgreSQL
  `clock_timestamp()` *after* the row lock is held, never from `DateTime.UtcNow` and never from a
  `now()` evaluated before the lock. `now()` is transaction-start time and would be stale after a wait.
- **Derived seat counts.** `classes` has no seat counter columns. `heldSeats`, `confirmedSeats` and
  `availableSeats` are computed from `holds` on every read, using the same database-time reference:
  `heldSeats = COUNT(status = 'HELD' AND expires_at > db_now)`,
  `confirmedSeats = COUNT(status = 'CONFIRMED')`.
- **Effective `EXPIRED`.** A hold is expired when `db_now >= expires_at`. Nothing writes `EXPIRED`; the
  status is derived on read. That is why there is **no background worker and no cleanup job**: an
  expired hold stops counting toward `heldSeats` the moment the clock passes its `expires_at`, with no
  request and no scheduler involved.
- **Idempotency.** A duplicate hold request returns the existing active hold with its original
  `expiresAt` (`200`); a repeated confirmation returns `200` and leaves exactly one confirmed row.
- **Frontend.** React 18 + TypeScript, TanStack Query for server state with 3-second polling on the
  class list, query keys scoped by student ID, and the student ID kept in `localStorage` so My
  Applications survives a browser restart.

## Known limitations

- **No authentication.** There is no login, no password and no role model. `X-Student-Id` is a claim,
  not a credential, and class creation is an open action. This is a demo of the assignment's contract,
  not a production-secure admission service.
- **Lock order is not HTTP arrival order.** Allocation order is the order in which requests acquire the
  PostgreSQL class row lock. That is a real serialised order and it is fair in the sense that no
  request is starved by design, but it is not a guarantee that the first request the web server
  received wins. Strict arrival-order FIFO would need an intake queue or a sequence number.
- **Open admin class creation.** Anyone who can reach the API can create a class. That is deliberate:
  the assignment specifies no login, so the admin screen is a demo action.
- **`VITE_API_BASE_URL` is a build-time value.** Changing it needs a rebuild, not a restart. The
  container build takes `http://localhost:8080` from `frontend/.env.production`, so the bundled UI
  calls the API directly across origins and depends on the API's CORS allowance for
  `FRONTEND_ORIGIN`. Running the frontend with `npm run dev` uses the Vite dev server's proxy for
  `/classes`, `/holds` and `/me` instead, so no CORS is involved in development.
- **Single school.** Class names are unique case-insensitively across the whole database, which is
  correct for one school and would need a school scope for more.

## Repository layout

```text
backend/            ASP.NET Core API (controllers, services, EF Core, migrations, Dockerfile)
backend.Tests/      xUnit integration tests against real PostgreSQL
frontend/           React + TypeScript + Vite app and its Dockerfile
docker-compose.yml  PostgreSQL, API and frontend
.env.example        Every environment variable with its default
PRD.md              The product requirements document this implementation follows
ASSUMPTIONS.md      Every gap in the source and the behaviour chosen to fill it
AI_USAGE.md         Which AI tools were used, and how the result was verified
```
