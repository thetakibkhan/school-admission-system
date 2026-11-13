# Product Requirements Document (PRD)

# School Admission System

**Project:** Onnorokom Projukti Limited — Assistant Software Engineer Take-Home Assignment  
**PRD version:** 1.0  
**Status:** Approved planning baseline (implementation not yet verified)  
**Prepared:** 13 November 2026  
**Source:** `Asst_Software_Engineer_Project_Assignment (1).pdf`, pages 1–5, plus the 22 design decisions agreed in this conversation  
**Assignment's stated deadline:** 11 November 2026, 11:59:59 PM (already past when this PRD was prepared)  
**Implementation stack:** C# / ASP.NET Core 8+, PostgreSQL, EF Core (Npgsql), React 18+, TypeScript, Vite, Docker Compose

> **Source-of-truth rule:** The assignment's mandatory functionality and API contract take precedence over design assumptions in this PRD. Requirements from the PDF are labeled **[SPEC]**; choices made during planning are labeled **[DECISION]**; gaps resolved here are labeled **[ASSUMPTION]**; optional improvements are labeled **[BONUS]**. Record material assumptions and their rationale in `ASSUMPTIONS.md`. Do not publish or commit the confidential assignment PDF to the public repository.

---

## 1. Executive summary

Build a small, reliable, first-come-first-served school admission system. A school administrator creates an independently seat-limited class/shift. A parent identifies a student using their 17-digit birth registration number, reserves an available seat, and may confirm simulated payment before the hold expires or cancel the hold. Expired holds release their seats automatically. The UI must show class availability and application status without requiring manual page reloads. The system must remain correct under simultaneous requests and persist its data across application restarts.

**Highest-priority outcome:** Never allocate more active holds plus confirmed admissions than a class's `totalSeats` — including during concurrent requests.

### 1.1 Goals

1. Prevent seat overselling under concurrency and retries.
2. Ensure holds expire after a configurable duration, with no request or cleanup job needed to release their capacity.
3. Enforce at most one active hold or confirmed admission per student per class.
4. Support safe, idempotent simulated payment confirmation.
5. Provide all required administrator and parent workflows in React.
6. Implement the eight specified HTTP endpoints and exact required JSON field names.
7. Start PostgreSQL, backend, and frontend using `docker compose up` and retain database data after normal Compose restarts.
8. Provide reproducible tests and clear delivery documentation.

### 1.2 Non-goals

- Real payment gateway, invoices, refunds, financial settlement, or payment webhooks.
- Student/administrator login, password, OTP, role-based access control, or identity verification; the assignment explicitly says no login. **Do not market this as a production-secure admission service.**
- Lottery, waiting lists, reservation extension, admission editing, capacity updates, batch imports, multiple schools, notifications, or dashboards not specified in the assignment.
- Guaranteed FIFO ordering at the network-request level. PostgreSQL transactional lock acquisition decides the effective allocation order; see §5.5.
- An expiration scheduler/worker, WebSockets, or SSE for the baseline.

### 1.3 Users and roles

| Actor | Core needs | Authentication in assignment |
|---|---|---|
| School administrator | Create class-shifts and inspect availability | No login; class creation is an accessible demo UI action. **Not a production authorization model.** |
| Parent / student representative | Enter student ID, reserve, confirm/cancel, inspect history | Student is identified with `X-Student-Id` (17-digit birth registration number). |
| Evaluator / developer | Run the project with Docker; call exact APIs; run repeatable tests | No extra host dependencies beyond Docker. |

### 1.4 Success / acceptance measures

| Measure | Pass condition |
|---|---|
| Capacity integrity | For every class: `heldSeats + confirmedSeats <= totalSeats` in all tests and sampled runtime results. |
| Concurrency | With capacity `N` and significantly more than `N` simultaneous distinct-student requests, exactly `N` or fewer succeed; no oversell. |
| Reservation duration | `HOLD_DURATION_SECONDS` defaults to 600; holds at or beyond `expiresAt` do not consume capacity. |
| Idempotency | Repeated confirmation does not create another admission; duplicate hold request returns the existing active hold. |
| API compatibility | All eight required paths/methods exist; documented success JSON has no renamed or missing required keys. |
| UI recovery | With a previously saved student ID, reopen browser and display the student's applications from PostgreSQL. |
| Live state | Class counts refresh via polling every 3 seconds without full page reload. |
| Persistence | Data remains after `docker compose down` followed by `docker compose up` (without `-v`). |
| Tests | At least three automated backend tests, including a concurrency test; broader recommended test matrix in Epic 10. |

---

## 2. Scope, priority and delivery order

**Priority:** `P0` = mandatory for assignment acceptance; `P1` = agreed implementation quality or user-facing detail; `BONUS` = optional, not a blocker for completion.

| Epic | Title | Priority | Dependencies |
|---|---|---|---|
| EP-01 | Project foundation, persistence and configuration | P0 | None |
| EP-02 | Class creation and seat availability | P0 | EP-01 |
| EP-03 | Student identity and application discovery | P0 | EP-01, EP-02 |
| EP-04 | Atomic seat reservation and duplicate protection | P0 | EP-01, EP-02, EP-03 |
| EP-05 | Hold expiration and accurate statuses | P0 | EP-04 |
| EP-06 | Admission confirmation and idempotency | P0 | EP-04, EP-05 |
| EP-07 | Hold cancellation and seat release | P0 | EP-04, EP-05 |
| EP-08 | React administrator/parent experience | P0 | EP-02–EP-07 |
| EP-09 | API contract, validation and error experience | P0 | EP-02–EP-08 |
| EP-10 | Automated testing and concurrency verification | P0 | EP-01–EP-09 |
| EP-11 | Documentation, Docker delivery and handoff | P0 | EP-01–EP-10 |
| EP-12 | Bonus: multi-process and frontend automated tests | BONUS | EP-10, EP-11 |

**Suggested vertical-slice order:** Complete a working API and persistence first; then class creation/listing; reservation; expiration; confirm/cancel; parent/applicant UI; validation; integration tests; submission checks. Do not postpone concurrency testing until the very end.

---

## 3. Approved decisions (Q1–Q22)

| # | Question | Chosen answer |
|---|---|---|
| Q1 | Prevent overselling under simultaneous requests? | PostgreSQL transactions and per-class `SELECT … FOR UPDATE`. |
| Q2 | Expire holds? | Timestamp-derived expiration; no background worker. |
| Q3 | Calculate available seats? | Derive counts from hold/admission records; no mutable seat counters. |
| Q4 | .NET data access? | EF Core / Npgsql; raw PostgreSQL SQL where locking requires it. |
| Q5 | Live class updates? | HTTP polling every 3 seconds. |
| Q6 | Duplicate active hold request? | Return the existing hold; preserve original expiry. |
| Q7 | Backend layout? | ASP.NET Core Controllers + Services. |
| Q8 | Frontend language? | React with TypeScript. |
| Q9 | Student recovery? | Store student ID in `localStorage`, with Change/Clear control. |
| Q10 | Core storage model? | `classes` and `holds` tables. |
| Q11 | Represent shifts? | Each class/shift is a separate class record. |
| Q12 | Model statuses? | `HELD`, `CONFIRMED`, `EXPIRED`, `CANCELLED`; derive `EXPIRED` by timestamp if still stored as `HELD`. |
| Q13 | Repeat confirm? | Idempotent; return already confirmed without double counting. |
| Q14 | Cancel semantics? | Active hold can be cancelled; repeat cancellation is safe; confirmed record cannot be cancelled. |
| Q15 | Error handling? | Validation + business checks, suitable HTTP 4xx, JSON Problem Details. |
| Q16 | Frontend server state? | TanStack Query, polling, query invalidation. |
| Q17 | Screen design? | Small responsive admin, class list, application detail, and My Applications screens. |
| Q18 | Styling? | Tailwind CSS and simple accessible React components. |
| Q19 | Backend test stack? | xUnit, real PostgreSQL-backed integration tests, concurrency test. |
| Q20 | Runtime deployment? | PostgreSQL, backend and frontend via Docker Compose + named DB volume. |
| Q21 | Login/payment scope? | No authentication; simulated payment; admin UI remains a demo-only open action. |
| Q22 | Extras? | Mandatory features first; frontend automated tests and two-process proof are optional. |

---

## 4. Product journeys

### 4.1 Administrator journey

1. Open the application and navigate to **Create Class**.
2. Enter class/shift name, e.g. `Class 6 - Morning Shift`, and `totalSeats = 120`.
3. Submit; the new class appears in the class listing with `heldSeats = 0`, `confirmedSeats = 0`, `availableSeats = 120`.
4. Watch counts update when students hold, confirm, cancel or expire.

### 4.2 Parent journey — successful admission

1. Enter the student's 17-digit birth registration number in the persistent identity field.
2. View classes and available seat counts.
3. Click **Apply** for a class with seats remaining.
4. See a `HELD` reservation, a countdown, **Pay Admission Fee** and **Cancel**.
5. Click **Pay Admission Fee** before expiration; simulated payment succeeds immediately.
6. See `CONFIRMED`. The seat stays occupied and cannot be cancelled through the required cancellation endpoint.

### 4.3 Parent journey — expiration / recovery

1. Parent creates a `HELD` reservation but does not pay.
2. At `expiresAt` the reservation becomes effectively `EXPIRED`, even if no backend request is made.
3. The parent UI changes to `EXPIRED` without reload and disables the payment button.
4. The seat becomes available to other students.
5. After browser reopening, the student's saved ID is restored and application history is re-fetched from the backend.

---

## 5. Domain model, invariants and rules

### 5.1 Entities

**`classes`**

| Field | Type | Rules |
|---|---|---|
| `id` | Unique ID (e.g. prefixed UUID/string) | Stable; example `c_123` is illustrative only. |
| `name` | String | Required; trimmed; unique case-insensitively within this single-school demo **[ASSUMPTION]**. |
| `total_seats` | Positive integer | `> 0`; immutable after creation because no class-update endpoint is specified. |
| `created_at` | UTC timestamp | Server generated. |

**`holds`** (single source of truth for holds and confirmed admissions)

| Field | Type | Rules |
|---|---|---|
| `id` | Unique ID | Stable; example `h_789` is illustrative. |
| `class_id` | FK → `classes.id` | Required and indexed. |
| `student_id` | 17-digit string | Preserve leading zeros; never store as a number. |
| `status` | `HELD`, `CONFIRMED`, `CANCELLED` | Persistent state; `EXPIRED` may be an effective/computed status. |
| `created_at` | UTC timestamp | Server generated. |
| `expires_at` | UTC timestamp | Set once at creation and never extended by retries. |
| `confirmed_at` | Nullable UTC timestamp | Set at first successful confirmation. |
| `cancelled_at` | Nullable UTC timestamp | Set at first successful cancellation. |

**[DECISION]** Keep stored status simple: `EXPIRED` is an effective status when `status = HELD` and `expires_at <= database current time`. No cleanup job is necessary. A later opportunistic migration of stored `HELD` to `EXPIRED` is optional and must not be relied on for seat release.

**Recommended indexes:** `(class_id, status, expires_at)`, `(student_id, created_at DESC)`, `(class_id, student_id)`. A database unique partial index for `(class_id, student_id)` where `status = CONFIRMED` adds defense against double confirmed admissions. **Do not** attempt a time-dependent partial unique index using `NOW()`; active-hold uniqueness must be checked while the class row is locked.

### 5.2 Derived seat counts

Use the same database-time reference within each seat calculation:

```text
heldSeats      = COUNT(holds WHERE class_id = C AND status = HELD
                       AND expires_at > db_now)
confirmedSeats = COUNT(holds WHERE class_id = C AND status = CONFIRMED)
availableSeats = totalSeats - heldSeats - confirmedSeats
```

**Invariant I-01:** `0 <= heldSeats + confirmedSeats <= totalSeats` for each class.  
**Invariant I-02:** At most one **currently active HELD** or **CONFIRMED** admission exists for the same `(class_id, student_id)`.  
**Invariant I-03:** A `CONFIRMED` admission continues to occupy a seat after its original `expiresAt`.  
**Invariant I-04:** An expired or cancelled hold does not occupy a seat.  
**Invariant I-05:** A hold's `expiresAt` never changes due to a duplicate request.  
**Invariant I-06:** Every successful confirmation changes a hold from effective `HELD` to `CONFIRMED` only once.

### 5.3 State transitions

| Current effective state | Action | Next state / result |
|---|---|---|
| No record | Successful new reservation | `HELD` |
| `HELD` before expiry | Confirm simulated payment | `CONFIRMED` |
| `HELD` before expiry | Cancel | `CANCELLED` |
| `HELD` reaches expiry | Passage of database time alone | `EXPIRED` (effective) |
| `CONFIRMED` | Confirm again by same student | `CONFIRMED`; idempotent `200` |
| `CONFIRMED` | Cancel | `409 Conflict` **[ASSUMPTION]** |
| `CANCELLED` | Cancel again by same student | `204 No Content` **[DECISION]** |
| `EXPIRED` | Confirm | `409 Conflict` **[ASSUMPTION]** |
| `EXPIRED` | Cancel by same student | `204 No Content`, no effect **[ASSUMPTION]** |
| `EXPIRED` or `CANCELLED` | Apply again | New hold if seats remain |
| Same class already `CONFIRMED` | Apply again | `409 Conflict` **[ASSUMPTION]** |

**Exact boundary:** A hold is eligible only when `db_now < expiresAt`. At `db_now >= expiresAt`, it is expired. Treat time from PostgreSQL as authoritative for allocation/confirmation rather than using the browser countdown.

### 5.4 Atomic reservation transaction (normative design)

```text
BEGIN;
SELECT id FROM classes WHERE id = :classId FOR UPDATE;
-- If missing: rollback → 404.
-- Obtain current PostgreSQL clock time AFTER acquiring the lock.
-- Check same student's existing active HELD and CONFIRMED records.
-- If existing active HELD: return it without insertion or extending expiry.
-- If existing CONFIRMED: rollback → 409.
-- Count active HELD and CONFIRMED for this class using fresh DB time.
-- If no seats: rollback → 409.
-- Insert new HELD, expires_at = decisionTime + HOLD_DURATION_SECONDS.
COMMIT;
```

To avoid a stale timestamp while waiting on the class lock, obtain a fresh PostgreSQL time (e.g. `clock_timestamp()`) **after** the lock is acquired. Under PostgreSQL `READ COMMITTED` isolation, counting after the lock is acquired sees earlier committed reservations. All operations affecting a class's occupied seats must follow the same per-class lock protocol.

### 5.5 Allocation order and fairness

**[SPEC]** First-come-first-served; no lottery. **[DECISION]** Process reservations in the order they acquire the PostgreSQL class-row lock. This is a practical serialized allocation order, **not a guarantee** that the first HTTP request received by the web server wins. State this limitation in `ASSUMPTIONS.md`; strict arrival-order FIFO would require an additional intake/sequence/queue design.

### 5.6 Ownership and student identity

- `X-Student-Id` is mandatory for `POST /classes/{classId}/holds`, `POST /holds/{holdId}/confirm`, `DELETE /holds/{holdId}`, and `GET /me/holds` as explicitly shown by the assignment.
- Student ID format: `^[0-9]{17}$` (ASCII digits); do not parse as a number.
- Confirmation and cancellation are allowed only for the matching student; for a wrong student use `404 Not Found` to avoid exposing another student's record **[ASSUMPTION]**.
- `GET /holds/{holdId}` does **not** explicitly require `X-Student-Id` in the provided API example; preserve assignment compatibility rather than introducing a mandatory header **[SPEC/ASSUMPTION]**. In a production system it would need proper authorization.
- The browser may store `studentId` for usability, but it is not authoritative for admission state; always request state from the API.

---

## 6. Required HTTP API contract

**Base URL:** `http://localhost:8080` **[SPEC]**  
**Format:** JSON request/response (except `204 No Content`), exact required success fields; additional fields are allowed by the source but should be avoided for simplicity.  
**Errors:** JSON Problem Details (`application/problem+json`) **[DECISION]**.

| # | Method | Route | Required success / behavior | Identity header |
|---|---|---|---|---|
| 1 | POST | `/classes` | `201`: `id`, `name`, `totalSeats` | None specified |
| 2 | GET | `/classes` | `200`: array of `id`, `name`, `totalSeats`, `heldSeats`, `confirmedSeats`, `availableSeats` | None specified |
| 3 | GET | `/classes/{classId}` | `200`: same class/seat-count fields | None specified |
| 4 | POST | `/classes/{classId}/holds` | `201`: `holdId`, `classId`, `studentId`, `status`, `expiresAt`; no seat → `409` | Required |
| 5 | GET | `/holds/{holdId}` | `200`: `holdId`, `classId`, `status`, `expiresAt` | None specified |
| 6 | POST | `/holds/{holdId}/confirm` | `200`: `holdId`, `status = CONFIRMED` | Required |
| 7 | DELETE | `/holds/{holdId}` | `204` on successful cancellation; no response body | Required |
| 8 | GET | `/me/holds` | `200`: array of `holdId`, `classId`, `className`, `status`, `expiresAt` | Required |

**Notes:**

1. For a repeated hold request, return `200 OK` with the same hold payload and original expiry **[DECISION]**. New hold remains `201 Created` **[SPEC]**.
2. For repeated confirmation of an already confirmed record by the same student, return `200` with `{ "holdId": "...", "status": "CONFIRMED" }` **[DECISION]**.
3. For a class not found, hold not found, malformed ID, missing identity header, expired confirmation, no seats, or student conflict, use appropriate 4xx responses. Only the no-seats `409` is specified explicitly; other precise mappings are assumptions.
4. Class creation request must accept exactly the documented field names `name` and `totalSeats`; do not rename `classId`, `holdId`, `heldSeats`, `confirmedSeats`, `availableSeats`, `expiresAt`, or `studentId` in success responses.
5. The example IDs (`c_123`, `h_789`) and timestamps in the PDF illustrate a shape, not a required ID algorithm or fixed time.

### 6.1 Sample request and response shapes

```http
POST /classes
Content-Type: application/json

{"name":"Class 6 - Morning Shift","totalSeats":120}
```

```json
{"id":"c_123","name":"Class 6 - Morning Shift","totalSeats":120}
```

```json
{"id":"c_123","name":"Class 6 - Morning Shift","totalSeats":120,"heldSeats":15,"confirmedSeats":80,"availableSeats":25}
```

```http
POST /classes/c_123/holds
X-Student-Id: 20152691234567890
```

```json
{"holdId":"h_789","classId":"c_123","studentId":"20152691234567890","status":"HELD","expiresAt":"2026-11-01T04:10:00Z"}
```

```json
{"holdId":"h_789","status":"CONFIRMED"}
```

### 6.2 Error-code policy (assumptions except where spec fixes the code)

| Situation | Code | UI message intent |
|---|---|---|
| Missing or invalid 17-digit student ID | 400 | Enter exactly 17 digits. |
| Invalid class name or `totalSeats` | 400 | Correct the highlighted fields. |
| Unknown class / unknown hold | 404 | Class or application not found. |
| Student mismatch on confirm/delete | 404 | Application not found for this student. |
| No seats remaining | 409 **[SPEC]** | All seats are currently occupied. |
| Same student already confirmed | 409 | Already admitted to this class. |
| Confirming expired or cancelled hold | 409 | Reservation can no longer be confirmed. |
| Cancelling confirmed admission | 409 | Confirmed admission cannot be cancelled here. |
| Duplicate class name | 409 | A class with this name already exists. |
| Unexpected server/database error | 500 / 503 | Temporary problem; please retry. Do not reveal stack traces. |

---

## 7. Epic backlog: user stories, acceptance criteria and epic Definition of Done

Each acceptance criterion is intended to be verifiable by automated tests or a repeatable UI walkthrough. Story priorities are `P0` unless otherwise stated.

### EP-01 — Project foundation, database and runtime configuration

**Epic objective:** Establish a bootable, configurable ASP.NET Core/React/PostgreSQL system with schema migrations and durable data.

#### US-01.1 — Bootstrap the backend and frontend

**As a** developer, **I want** separate .NET and React projects **so that** the application can be built and deployed consistently.

**Acceptance criteria**
- [ ] `backend/` contains an ASP.NET Core 8+ API with Controllers, Services, EF Core/Npgsql setup and a compilable entry point.
- [ ] `frontend/` contains React 18+ with TypeScript and Vite; build succeeds.
- [ ] Backend binds on `0.0.0.0:8080` inside its container and is reachable at `http://localhost:8080` from the host.
- [ ] Backend's PostgreSQL connection string and frontend's API URL are configurable through environment variables, not hard-coded machine-local paths.

#### US-01.2 — Create the database schema and indexes

**As a** developer, **I want** versioned migrations for classes and holds **so that** state can be created and evolved reliably.

**Acceptance criteria**
- [ ] EF Core migrations create `classes` and `holds` with the columns and referential integrity in §5.1.
- [ ] Database enforces positive `total_seats`, non-null class/student IDs, and valid persisted states.
- [ ] Indexes support availability counting and `/me/holds` efficiently.
- [ ] Migrations run during startup or through a documented startup migration step requiring no host-side .NET installation.

#### US-01.3 — Configure reservation duration

**As a** school operator, **I want** hold duration configurable **so that** the admission window can be adjusted without code changes.

**Acceptance criteria**
- [ ] `HOLD_DURATION_SECONDS` defaults to `600` when omitted.
- [ ] A positive integer override changes expiry for **new** holds.
- [ ] Zero, negative, or malformed configuration fails startup with a clear log; it does not silently create invalid holds.
- [ ] Runtime always saves UTC timestamps and serializes expiry in ISO 8601 with `Z`.

#### US-01.4 — Preserve data across restarts

**As an** evaluator, **I want** application data to survive restarts **so that** previously created classes and admissions remain available.

**Acceptance criteria**
- [ ] PostgreSQL uses a named Docker volume.
- [ ] Create a class and hold; run `docker compose down`, then `docker compose up`; data remains available.
- [ ] The instructions do not run `docker compose down -v` as the normal shutdown procedure.

**Epic-level Definition of Done — EP-01**
- [ ] All four stories are accepted and verified on a clean Docker environment.
- [ ] DB migrations and environment configuration are reproducible and documented.
- [ ] No secrets or real student records are committed; sample configuration is safe.
- [ ] Rebooting services does not lose database state.

---

### EP-02 — Class administration and seat availability

**Epic objective:** Allow the administrator to create independently seat-limited class/shift records and let users see accurate capacity.

#### US-02.1 — Create a class/shift

**As a** school administrator, **I want** to create a named class with a seat limit **so that** parents can apply to it.

**Acceptance criteria**
- [ ] A Create Class UI form accepts `name` and `totalSeats` and sends `POST /classes`.
- [ ] A valid request returns `201` with `id`, `name`, `totalSeats` and persists the record.
- [ ] `totalSeats` must be a positive whole number; blank name and invalid values return 4xx with actionable feedback.
- [ ] `Class 6 - Morning Shift` and `Class 6 - Day Shift` can be created as two independent records.

#### US-02.2 — Prevent duplicate class identities

**As an** administrator, **I want** accidental duplicate class names blocked **so that** the listing remains unambiguous.

**Acceptance criteria**
- [ ] Names are trimmed for validation and storage.
- [ ] Matching normalized class names (case-insensitive) return `409` rather than creating a second class **[ASSUMPTION]**.
- [ ] Different shift names are accepted as separate classes.
- [ ] A duplicate-class failure does not alter any existing class.

#### US-02.3 — List class capacities

**As a** parent, **I want** to see each class's seat counts **so that** I know where I can apply.

**Acceptance criteria**
- [ ] `GET /classes` returns all created classes with the six mandatory fields and `200`.
- [ ] A newly created class shows `heldSeats=0`, `confirmedSeats=0`, and `availableSeats=totalSeats`.
- [ ] An empty database returns `[]`, not a 500 or an unrelated placeholder.
- [ ] Different class-shifts never share seat counts.

#### US-02.4 — Inspect a single class

**As a** parent, **I want** the current availability of one class **so that** I can check the class before applying.

**Acceptance criteria**
- [ ] `GET /classes/{classId}` returns `200` with the exact six specified fields for a valid class.
- [ ] Unknown IDs return a structured `404`.
- [ ] Active holds reduce availability; confirmations continue occupying seats; expired/cancelled holds do not.
- [ ] Counts satisfy the invariant in §5.2.

**Epic-level Definition of Done — EP-02**
- [ ] Admin class creation works through the React UI and through the specified API.
- [ ] Both class GET endpoints return exact required success fields with correct derived counts.
- [ ] Class shifts have independent capacity and duplicate handling is tested.
- [ ] No database editing or Postman steps are required for normal class creation.

---

### EP-03 — Student identification and application history

**Epic objective:** Implement the assignment's no-login, 17-digit student identity flow and history recovery.

#### US-03.1 — Identify a student

**As a** parent, **I want** to enter the student's birth registration number **so that** admission actions are associated with the correct student.

**Acceptance criteria**
- [ ] A clearly labeled student-ID field is present near the top of the application.
- [ ] Input is treated as a string and accepts exactly 17 ASCII digits, including leading zeros.
- [ ] Student-scoped API calls include `X-Student-Id` with the active student's ID.
- [ ] Blank, non-digit, too-short, and too-long values show clear validation errors; backend validates independently.

#### US-03.2 — Retain or switch student identity

**As a** parent using the same device, **I want** to resume or change the student ID **so that** I can check an earlier application or use another student's ID.

**Acceptance criteria**
- [ ] An entered valid student ID is saved in `localStorage` **[DECISION]**.
- [ ] Browser reload or restart restores the ID and triggers a fresh history request.
- [ ] **Change Student / Clear ID** removes the stored ID and clears all student-specific cached API data before showing another student's data.
- [ ] Student ID is not embedded in the URL or logged in normal client debug output.

#### US-03.3 — Retrieve all of a student's applications

**As a** parent, **I want** a My Applications page **so that** I can review my holds and confirmed admissions.

**Acceptance criteria**
- [ ] `GET /me/holds` returns `200` with a JSON array of `holdId`, `classId`, `className`, `status`, `expiresAt`.
- [ ] Only rows for the `X-Student-Id` value are included.
- [ ] Active, confirmed, cancelled, and effective-expired records can all appear in the history **[ASSUMPTION]**.
- [ ] No applications returns `[]` and a useful empty state in the UI.
- [ ] History is fetched from the backend, not reconstructed from browser storage.

**Epic-level Definition of Done — EP-03**
- [ ] Every required student-scoped action passes and validates `X-Student-Id`.
- [ ] Recovery works after page refresh and browser reopening.
- [ ] Switching IDs clears student-specific cached state and does not accidentally reuse prior student's header.
- [ ] Invalid IDs and empty histories have testable user-friendly behavior.

---

### EP-04 — Concurrency-safe seat reservation and duplicate prevention

**Epic objective:** Reserve seats safely, even when many different students and/or repeat requests arrive simultaneously.

#### US-04.1 — Reserve an available seat

**As a** parent, **I want** to reserve one seat in a class **so that** I have time to confirm admission.

**Acceptance criteria**
- [ ] `POST /classes/{classId}/holds` with a valid `X-Student-Id` creates a `HELD` record and returns `201` with the five required fields.
- [ ] `expiresAt` equals the database decision time plus `HOLD_DURATION_SECONDS` (subject to timestamp precision).
- [ ] An active new hold increments `heldSeats` by 1 and decrements `availableSeats` by 1 for that class.
- [ ] Unknown class ID returns `404`; invalid student ID returns `400`.

#### US-04.2 — Prevent overselling with database locking

**As a** school operator, **I want** reservation updates serialized per class **so that** the total number of allocated seats cannot exceed capacity.

**Acceptance criteria**
- [ ] Each reservation transaction locks its class row using PostgreSQL `FOR UPDATE` before checking capacity and inserting.
- [ ] Capacity count and insertion occur inside one database transaction.
- [ ] With `N=10` seats and `M=50` distinct students sending concurrent requests, no more than 10 active reservations succeed.
- [ ] Rejected excess requests return `409`; count remains within the invariant after all requests finish.
- [ ] Requests for different classes can proceed independently; no accidental global lock is introduced.

#### US-04.3 — Reuse an existing active hold on retry

**As a** parent, **I want** an accidental repeat application to return my existing reservation **so that** I don't lose time or duplicate seats.

**Acceptance criteria**
- [ ] If the same student already has an unexpired `HELD` for that class, return its same `holdId` and unchanged `expiresAt` with `200` **[DECISION]**.
- [ ] Duplicate calls do not create an extra row representing a new active hold or consume a second seat.
- [ ] If another hold for the student/class is `CONFIRMED`, return `409` **[ASSUMPTION]**.
- [ ] After an old hold is expired or cancelled, the student can submit a new hold request if capacity remains.

#### US-04.4 — Enforce per-student uniqueness under races

**As a** school operator, **I want** simultaneous retries by the same student to be safe **so that** there is never more than one active or confirmed admission for a class.

**Acceptance criteria**
- [ ] Two parallel hold requests for the same student/class produce one active hold and both responses refer to that hold (new `201`, repeat `200`).
- [ ] The duplicate-check runs after locking the class row, not in an unprotected precheck.
- [ ] A failed or rolled-back reservation leaves seat counts unchanged.
- [ ] A confirmed record blocks further active reservations in that class.

**Epic-level Definition of Done — EP-04**
- [ ] All capacity-changing reservation paths use class-row locks and transactions.
- [ ] Concurrency tests demonstrate `heldSeats + confirmedSeats <= totalSeats`.
- [ ] Same-student duplicate and retry behavior is idempotent without extending expiration.
- [ ] `409` for no seats is returned consistently, including under load.

---

### EP-05 — Hold expiration, status and countdown

**Epic objective:** Expire holds automatically by time, release capacity immediately, and reflect their effective status.

#### US-05.1 — Expire a hold without a cleanup job

**As a** school operator, **I want** the reservation to stop consuming capacity at expiration **so that** unused seats return to the pool automatically.

**Acceptance criteria**
- [ ] When PostgreSQL time reaches `expiresAt`, a stored `HELD` is treated as effective `EXPIRED` without any API call or worker.
- [ ] Availability queries exclude the expired hold from `heldSeats` and include the returned seat in `availableSeats`.
- [ ] A new student can reserve the released seat without requiring an earlier GET/cleanup call.
- [ ] At the exact expiry boundary, the hold cannot be confirmed.

#### US-05.2 — Report effective reservation status

**As a** parent, **I want** accurate status via API **so that** I know whether my application is still actionable.

**Acceptance criteria**
- [ ] `GET /holds/{holdId}` returns the required `holdId`, `classId`, effective `status`, `expiresAt` fields.
- [ ] `GET /me/holds` also reports effective `EXPIRED` for old unconfirmed holds.
- [ ] Valid status values are only `HELD`, `CONFIRMED`, `EXPIRED`, `CANCELLED`.
- [ ] Once a hold is `CONFIRMED`, its old expiry timestamp never makes it appear `EXPIRED`.

#### US-05.3 — Show countdown and expire UI in place

**As a** parent, **I want** to see my remaining hold time **so that** I can confirm before the deadline.

**Acceptance criteria**
- [ ] While `HELD`, the application UI displays remaining time based on `expiresAt` and refreshes the countdown at least once per second **[DECISION]**.
- [ ] At zero, UI changes to `EXPIRED` and disables **Pay Admission Fee** without a full page reload.
- [ ] Client display is reconciled with API status; server always makes the final eligibility decision.
- [ ] On browser reload, existing expiry is used; countdown never resets to the full configured duration.

**Epic-level Definition of Done — EP-05**
- [ ] Expiration requires no worker, browser event or request to release capacity.
- [ ] API status, seat counts and UI status agree after expiry.
- [ ] Boundary tests show expired holds cannot be confirmed or counted as held.
- [ ] Restart and browser refresh do not extend a hold's expiration.

---

### EP-06 — Confirm admission and idempotent simulated payment

**Epic objective:** Convert an eligible hold into a permanent confirmed admission exactly once.

#### US-06.1 — Confirm a student's own active hold

**As a** parent, **I want** to confirm admission by clicking Pay Admission Fee **so that** the student's seat is secured.

**Acceptance criteria**
- [ ] `POST /holds/{holdId}/confirm` requires `X-Student-Id` matching the hold's student.
- [ ] A valid, unexpired `HELD` transitions to `CONFIRMED` and returns `200` with `holdId`, `status`.
- [ ] The simulated payment is considered successful immediately; no payment provider or external payment record is required.
- [ ] The seat moves from `heldSeats` to `confirmedSeats`; total occupied seats stay unchanged.

#### US-06.2 — Make confirmation idempotent

**As a** parent, **I want** confirmation retries to be safe **so that** double-clicks or network retries don't double-book.

**Acceptance criteria**
- [ ] Repeated confirmation of an already `CONFIRMED` hold by the same student returns `200` and `CONFIRMED`.
- [ ] `confirmedSeats` increases only on the first valid state transition.
- [ ] Two concurrent confirmation requests for the same hold produce one confirmed admission.
- [ ] The service uses the same per-class lock discipline for confirmation as for hold creation.

#### US-06.3 — Reject invalid confirmation attempts

**As a** school operator, **I want** invalid confirmations blocked **so that** seats are not granted after expiry or to the wrong student.

**Acceptance criteria**
- [ ] Effective `EXPIRED` and stored `CANCELLED` holds cannot be confirmed; return `409` **[ASSUMPTION]**.
- [ ] Wrong student ID cannot confirm another student's hold; return `404` **[ASSUMPTION]**.
- [ ] Unknown hold ID returns `404`; invalid student ID returns `400`.
- [ ] Failed attempts never change admission state or seat counts.

**Epic-level Definition of Done — EP-06**
- [ ] Confirmation is successful only for an unexpired owned hold or is a safe retry of already-confirmed state.
- [ ] Confirm/expire and confirm/confirm race tests pass.
- [ ] No real payment credentials, gateways or mock external callbacks are required.
- [ ] API and UI reflect confirmed status and correct counts without reload.

---

### EP-07 — Hold cancellation and immediate seat release

**Epic objective:** Let a student abandon an active reservation without affecting confirmed admissions.

#### US-07.1 — Cancel an active hold

**As a** parent, **I want** to cancel my reservation **so that** another student can take the seat.

**Acceptance criteria**
- [ ] `DELETE /holds/{holdId}` requires the owning student's valid `X-Student-Id`.
- [ ] An unexpired `HELD` transitions to `CANCELLED`, returns `204`, and has no response body.
- [ ] The cancelled hold immediately stops contributing to `heldSeats`.
- [ ] The cancellation action is available in the UI when status is `HELD`.

#### US-07.2 — Make cancellation safe to retry

**As a** parent, **I want** cancellation retries to have no additional effect **so that** a slow response doesn't cause confusion.

**Acceptance criteria**
- [ ] Repeated DELETE for an owned already-`CANCELLED` hold returns `204` **[DECISION]**.
- [ ] DELETE of an owned already-`EXPIRED` hold also returns `204` with no state/count change **[ASSUMPTION]**.
- [ ] Cancellation never produces negative seat counts or frees the same seat twice.
- [ ] Wrong-student or unknown hold requests cannot mutate a record.

#### US-07.3 — Protect confirmed admissions from cancellation

**As a** school operator, **I want** confirmed admissions protected **so that** a permanent seat cannot be released by a hold-cancellation endpoint.

**Acceptance criteria**
- [ ] DELETE of a `CONFIRMED` hold returns `409` **[ASSUMPTION]**.
- [ ] Its `confirmedSeats` count remains unchanged.
- [ ] Confirm-versus-cancel requests for the same hold are serialized by class locking; only one state-changing transition succeeds.
- [ ] UI does not render an actionable Cancel button for `CONFIRMED` holds.

**Epic-level Definition of Done — EP-07**
- [ ] Cancellation fulfills the API's `204` contract for valid active holds.
- [ ] Cancelled seats reappear immediately in derived availability.
- [ ] Concurrent confirm/cancel tests never produce inconsistent final state.
- [ ] Repeat DELETE and confirmed-cancel behavior are documented and tested.

---

### EP-08 — React UI, live availability and user experience

**Epic objective:** Make every required task available through an understandable UI, not just through Postman.

#### US-08.1 — Navigate between required screens

**As a** user, **I want** clear navigation **so that** I can create classes, browse classes, apply, and view my applications.

**Acceptance criteria**
- [ ] UI exposes Create Class, Class List, Hold/Application Detail and My Applications workflows.
- [ ] Every mandatory action (create, view, reserve, confirm, cancel, inspect history) can be completed using UI alone.
- [ ] UI is usable on typical mobile and desktop widths, with accessible labels and visible button states.
- [ ] Loading, empty, error and successful states are explicitly rendered; no blank-page failure.

#### US-08.2 — Show continuously refreshed availability

**As a** parent, **I want** seat counts to refresh automatically **so that** I can see reservations made by others.

**Acceptance criteria**
- [ ] Class listing uses TanStack Query to request `GET /classes` every 3 seconds while the page is active.
- [ ] Updated `heldSeats`, `confirmedSeats`, and `availableSeats` render without full reload.
- [ ] Successful reservation, cancellation and confirmation invalidate/re-fetch related class and hold queries immediately.
- [ ] API remains authoritative: a stale UI count never overrides a failed `409` reservation response.

#### US-08.3 — Present actionable hold details

**As a** parent, **I want** a clear reservation detail screen **so that** I can decide to confirm or cancel.

**Acceptance criteria**
- [ ] Detail page displays class, current status, expiry and countdown when applicable.
- [ ] **Pay Admission Fee** and **Cancel** appear only for effective `HELD`; both are disabled while their requests are pending.
- [ ] Confirmation visibly changes status to `CONFIRMED`; cancellation visibly changes it to `CANCELLED`.
- [ ] On `EXPIRED`, payment is disabled and the page explains how to apply again if seats become available.

#### US-08.4 — Display understandable errors and retries

**As a** parent, **I want** clear feedback when an action fails **so that** I know what to do next.

**Acceptance criteria**
- [ ] `400`, `404`, `409` and network/server failures have distinct, non-technical messages with appropriate next steps.
- [ ] A server-offline error offers a retry instead of a blank screen.
- [ ] Repeat application returning an existing hold opens that hold, not an error banner.
- [ ] Invalid client inputs are highlighted, but server validation still runs.

**Epic-level Definition of Done — EP-08**
- [ ] All four required screen types are connected to the eight APIs.
- [ ] No user operation depends on Postman or manual DB edits.
- [ ] Polling, time-based status changes, refresh recovery and action feedback work in browser walkthroughs.
- [ ] State changes remain correct even if cached data was briefly stale.

---

### EP-09 — API compatibility, validation and predictable errors

**Epic objective:** Make the public HTTP interface consistent with the assignment and resilient to malformed input.

#### US-09.1 — Preserve the specified request/response contract

**As an** evaluator, **I want** each endpoint to use exactly the required path and response keys **so that** automated API tests can interact with the system.

**Acceptance criteria**
- [ ] All eight routes, methods, required headers and documented success status codes from §6 are present.
- [ ] Exact casing is used for all required JSON fields; no field is removed or renamed.
- [ ] Success responses use JSON, except successful `DELETE` which uses empty-body `204`.
- [ ] UTC `expiresAt` values are valid ISO 8601 strings; statuses are uppercase as specified.

#### US-09.2 — Validate input and map domain failures

**As an** API client, **I want** invalid requests rejected consistently **so that** I can correct them safely.

**Acceptance criteria**
- [ ] Missing/invalid student IDs, invalid class fields and malformed requests produce 4xx (never 500 for expected bad input).
- [ ] No seats returns `409` as required by the assignment.
- [ ] Invalid state transitions and duplicate confirmed applications produce the documented 4xx mapping.
- [ ] Errors use structured JSON with stable status and a short safe description; raw stack traces and connection strings are never sent to clients.

#### US-09.3 — Keep database transactions and HTTP semantics aligned

**As a** developer, **I want** atomic operations with predictable HTTP results **so that** API callers can retry safely.

**Acceptance criteria**
- [ ] Transaction rollback occurs for any failed reservation, confirmation or cancellation before commit.
- [ ] Class row is locked before evaluating capacity or mutating occupancy-affecting state.
- [ ] Duplicate hold and repeated confirm responses are implemented according to approved decisions rather than inadvertently creating new rows.
- [ ] Operations honor the same expiration boundary and ownership rules across all endpoints.

**Epic-level Definition of Done — EP-09**
- [ ] A contract test checks every required route and required response field.
- [ ] Malformed input, missing records, conflicts and unauthorized-as-student actions return expected 4xx responses.
- [ ] No business-expected failure leaks as an unhandled 500.
- [ ] All HTTP behaviors not fixed by the source are explained in `ASSUMPTIONS.md`.

---

### EP-10 — Automated testing, edge cases and correctness evidence

**Epic objective:** Prove database concurrency safety, state lifecycle correctness and endpoint compatibility with reproducible automated tests.

#### US-10.1 — Test seat capacity under concurrency

**As an** evaluator, **I want** an automated simultaneous-reservation test **so that** I can verify the system never oversells.

**Acceptance criteria**
- [ ] Create a test class of capacity 10, issue 50 concurrent hold requests with distinct student IDs.
- [ ] At most 10 requests return a newly created `201` hold; excess requests fail with `409`.
- [ ] Database/API state reports no more than 10 active holds and zero negative available seats.
- [ ] Test uses real PostgreSQL transactions; EF Core InMemory is **not** used to claim row-lock correctness.

#### US-10.2 — Test expiration and seat reuse

**As a** developer, **I want** automated tests of expiry **so that** seats release without cleanup and expired records cannot confirm.

**Acceptance criteria**
- [ ] With a short configured duration, hold expires and no longer counts as occupied without any cleanup API.
- [ ] A different student can acquire the released seat.
- [ ] Confirming the expired hold returns `409`; `GET /holds/{holdId}` reports `EXPIRED`.
- [ ] A `CONFIRMED` record remains confirmed even after its original expiry timestamp passes.

#### US-10.3 — Test idempotency, ownership and races

**As a** developer, **I want** coverage of repeated actions **so that** retries cannot corrupt admission state.

**Acceptance criteria**
- [ ] Same-student concurrent duplicate hold calls result in one active reservation.
- [ ] Repeated and simultaneous confirm calls produce one confirmed admission.
- [ ] Concurrent confirm-versus-cancel ends in one valid final state and consistent seat counts.
- [ ] Wrong-student confirm/delete fail without mutating the hold.

#### US-10.4 — Test API contract, persistence and invalid inputs

**As an** evaluator, **I want** repeatable integration and smoke tests **so that** I can trust the submission end-to-end.

**Acceptance criteria**
- [ ] At least three automated backend tests run by a documented command, including the mandatory concurrency test **[SPEC]**.
- [ ] Endpoint contract checks verify required method/path/status/JSON fields.
- [ ] Invalid class inputs and malformed 17-digit student IDs return 4xx, not 500.
- [ ] A documented manual or automated smoke check confirms state survives a normal Compose down/up cycle.

**Epic-level Definition of Done — EP-10**
- [ ] Required backend tests pass against PostgreSQL in a clean environment.
- [ ] At least one test provides explicit evidence of no overselling under simultaneous requests.
- [ ] Expiry, duplicate holds, repeat confirmation, cancellation and ownership paths are covered.
- [ ] Tests are deterministic enough to rerun (isolation, unique sample IDs and cleanup documented).

---

### EP-11 — Docker delivery, documentation, AI disclosure and handoff

**Epic objective:** Deliver a self-contained, inspectable and runnable GitHub repository that meets the assignment's handoff rules.

#### US-11.1 — Start the full stack through Docker Compose

**As an** evaluator, **I want** one command to start everything **so that** I don't need local .NET, Node or PostgreSQL installations.

**Acceptance criteria**
- [ ] Repository root contains `docker-compose.yml` with PostgreSQL, API and frontend services.
- [ ] `backend/Dockerfile` and `frontend/Dockerfile` exist and build from source.
- [ ] `docker compose up --build` starts all services; backend is reachable at `http://localhost:8080` and the frontend at the README-documented host URL.
- [ ] Database volume survives ordinary `docker compose down`/`up`; startup sequencing/health checks avoid fragile first-run behavior.

#### US-11.2 — Write README and explicit assumptions

**As an** evaluator, **I want** clear setup and architecture notes **so that** I can run and understand the project.

**Acceptance criteria**
- [ ] `README.md` includes Docker startup/shutdown, frontend URL, backend URL, environment variables and test commands.
- [ ] `ASSUMPTIONS.md` records source gaps and chosen behavior: duplicate hold `200`, class-name uniqueness, errors, expiration, cancellation idempotency, no-auth demo and lock-order fairness limitation.
- [ ] `PRD.md` (this document) is placed in repository root as extra planning documentation.
- [ ] Any test fixtures or seeded data are documented and do not depend on an external service.

#### US-11.3 — Disclose AI assistance accurately

**As an** evaluator, **I want** a concise AI usage record **so that** I can assess how the work was produced and verified.

**Acceptance criteria**
- [ ] `AI_USAGE.md` lists tools actually used and the project parts they assisted with.
- [ ] It describes a real error/incorrect suggestion found during development and how it was identified and corrected, **if one occurred**; do not fabricate a mistake.
- [ ] It explains the human verification performed for important business rules and concurrency tests.
- [ ] No API keys, prompts containing secrets or personal student data are included.

#### US-11.4 — Deliver a clean public Git repository

**As an** evaluator, **I want** a reviewable public repository **so that** I can run and inspect the completed solution.

**Acceptance criteria**
- [ ] Git repository is public, with `backend/`, `frontend/`, a root Compose file and meaningful incremental commits.
- [ ] No `.env` secrets, database dumps, `node_modules`, build output or confidential assignment PDF are committed.
- [ ] A clean-clone check follows the README to start the app and run tests.
- [ ] Repository is ready for the submission process described in the assignment; actual external submission is a separate manual action.

**Epic-level Definition of Done — EP-11**
- [ ] Clone → `docker compose up --build` → browse app → test APIs works using only Docker on the host.
- [ ] README, ASSUMPTIONS, AI_USAGE, Dockerfiles and Compose file are accurate and checked against current code.
- [ ] Git history is incremental and free of leaked secrets and confidential PDF content.
- [ ] Repo is ready for evaluation; submission URL and deadline instructions are noted separately, without claiming submission happened.

---

### EP-12 — Optional bonus: multi-process proof and frontend tests

**Epic objective:** Add extra evidence of correctness without delaying mandatory acceptance.

#### US-12.1 — Verify capacity across two API processes **[BONUS]**

**As an** evaluator, **I want** a two-process test against one PostgreSQL database **so that** I can see the locks work across independent backend instances.

**Acceptance criteria**
- [ ] Two backend instances on different host ports share the same PostgreSQL database.
- [ ] Concurrent hold requests distributed across both instances never exceed `totalSeats`.
- [ ] Test steps, ports and results are documented in README or ASSUMPTIONS.

#### US-12.2 — Add frontend automated tests **[BONUS]**

**As a** developer, **I want** tests for critical UI states **so that** refresh, countdown and error feedback are less likely to regress.

**Acceptance criteria**
- [ ] Automated tests cover at least: invalid student ID, `HELD` to `EXPIRED` UI transition, and `409` no-seats message.
- [ ] Test command is documented and executes reproducibly.
- [ ] Tests do not assume frontend clock controls the server's reservation eligibility.

**Epic-level Definition of Done — EP-12 (bonus only)**
- [ ] Both bonus stories pass if this epic is undertaken.
- [ ] Mandatory P0 epics remain green and unchanged.
- [ ] Bonus work is clearly marked optional, not represented as a source requirement.

---

## 8. Global Definition of Done

A **P0 user story** is done only when:

- [ ] Implementation meets every listed acceptance criterion.
- [ ] The relevant API contract and backend validation are preserved.
- [ ] Database transaction behavior is correct wherever occupied seats can change.
- [ ] User-facing success, loading, empty and failure states are usable when the story has UI impact.
- [ ] Tests pass for normal flow plus meaningful error/boundary cases.
- [ ] No secrets or student identity details are leaked to logs, URLs or checked-in files.
- [ ] Code is committed with a focused, descriptive message and documentation is updated when behavior changes.

The **P0 release** is done only when all P0 epic-level Definitions of Done are met and the clean-clone Docker smoke test passes. Bonus EP-12 does not block P0 release.

---

## 9. Test scenario matrix (minimum recommended)

| Test ID | Scenario | Expected outcome | Related epic |
|---|---|---|---|
| T-01 | Create valid class | `201`, exact fields, persistent class | EP-02, EP-09 |
| T-02 | Create class with `totalSeats=0` | `400`, no inserted class | EP-02, EP-09 |
| T-03 | Create two different shifts | Independent class IDs and capacities | EP-02 |
| T-04 | List classes with no data | `200 []` | EP-02 |
| T-05 | One seat, two distinct simultaneous applicants | One `201`; one `409`; occupancy 1 | EP-04 |
| T-06 | Ten seats, fifty simultaneous applicants | At most ten successful new holds; no oversell | EP-04, EP-10 |
| T-07 | Same student retries active hold | Existing `holdId`, same expiry, no extra occupancy | EP-04 |
| T-08 | Same student applies after confirmation | `409`, no second hold | EP-04, EP-06 |
| T-09 | Hold reaches expiry, with no cleanup call | `EXPIRED`; seat available to new applicant | EP-05 |
| T-10 | Confirm before expiry | `200 CONFIRMED`; occupied count unchanged | EP-06 |
| T-11 | Confirm exactly at/after expiry | `409`; no admission | EP-05, EP-06 |
| T-12 | Confirm same hold twice | Both `200`; one confirmed admission | EP-06 |
| T-13 | Confirm by wrong student | 4xx; unchanged state | EP-03, EP-06 |
| T-14 | Cancel held reservation | `204`; seat released | EP-07 |
| T-15 | Cancel already cancelled reservation | `204`; no side effect | EP-07 |
| T-16 | Cancel confirmed admission | `409`; seat stays confirmed | EP-07 |
| T-17 | Concurrent confirm and cancel | One valid terminal state; correct counts | EP-06, EP-07 |
| T-18 | Invalid/missing student ID | `400`, no data change | EP-03, EP-09 |
| T-19 | Poll class list while another student applies | Counts update without full reload | EP-08 |
| T-20 | Refresh/reopen browser | Saved ID restored; applications fetched from API | EP-03, EP-08 |
| T-21 | Expired countdown | `EXPIRED` visible; payment disabled | EP-05, EP-08 |
| T-22 | Stop API / network error | Human-readable UI error and retry | EP-08 |
| T-23 | Normal Docker down/up | Data remains | EP-01, EP-11 |
| T-24 | Two API processes, one DB | Capacity never exceeded | EP-12 (BONUS) |

**Clock-testing note:** Prefer a short configurable hold duration or controllable test clock where practical. Never use arbitrary sleeps as the sole proof of transaction correctness. PostgreSQL-based concurrency tests should use distinct DB records and clear test isolation.

---

## 10. Engineering architecture and conventions

### 10.1 Proposed repository structure

```text
school-admission-system/
├── backend/
│   ├── Controllers/
│   │   ├── ClassesController.cs
│   │   ├── HoldsController.cs
│   │   └── MeController.cs
│   ├── Services/
│   │   ├── ClassService.cs
│   │   └── HoldService.cs
│   ├── Data/
│   │   ├── AdmissionDbContext.cs
│   │   └── Migrations/
│   ├── Models/
│   ├── Dtos/
│   ├── Validation/
│   ├── Program.cs
│   └── Dockerfile
├── backend.Tests/
│   ├── ApiContractTests.cs
│   ├── HoldLifecycleTests.cs
│   └── ConcurrencyTests.cs
├── frontend/
│   ├── src/
│   │   ├── api/
│   │   ├── components/
│   │   ├── pages/
│   │   ├── hooks/
│   │   └── types/
│   └── Dockerfile
├── docker-compose.yml
├── .env.example
├── .gitignore
├── README.md
├── ASSUMPTIONS.md
├── AI_USAGE.md
└── PRD.md
```

**Note:** Assignment requires a `backend/` and `frontend/` directory. A sibling `backend.Tests/` project is an internal organization choice; tests may instead reside under `backend/Tests/` if the repository tooling prefers it.

### 10.2 Backend guidelines

- Controllers handle HTTP request mapping, responses and lightweight validation.
- Services handle transactions, lock acquisition, capacity, ownership, expiration and state transitions.
- EF Core handles entities, migrations and standard queries; raw SQL gets the class row lock.
- Use `READ COMMITTED` with a fresh availability query executed **after** acquiring the class row lock.
- For confirm/cancel, read only enough to discover the `class_id`, acquire that class lock, then **re-fetch and validate** the hold after locking to avoid using a stale pre-lock status.
- Avoid a mutable `confirmed_seats` or `held_seats` counter in `classes`.
- Keep `expiresAt` immutable once the hold is created.

### 10.3 Frontend guidelines

- Centralize the HTTP client and inject the current `X-Student-Id` for student-scoped calls.
- Use TanStack Query for cached server state, polling and mutation invalidation.
- Use separate query keys by student ID so switching students cannot display old cached data.
- Base countdown display on server-provided UTC `expiresAt`; treat browser time as UI-only.
- Keep form validation helpful, but never rely on it instead of backend validation.

### 10.4 Environment variables

| Variable | Default / requirement | Used by |
|---|---|---|
| `HOLD_DURATION_SECONDS` | `600` | API |
| `ConnectionStrings__DefaultConnection` (or documented equivalent) | Configurable; PostgreSQL service address inside Compose | API |
| `ASPNETCORE_URLS` | `http://0.0.0.0:8080` | API |
| `VITE_API_BASE_URL` (or documented equivalent) | Backend's reachable browser URL, e.g. `http://localhost:8080` | Frontend build/runtime configuration |
| PostgreSQL database/user/password vars | Defined via Compose + `.env.example`; no committed real credentials | Database / Compose |

**Vite note:** `VITE_*` variables are ordinarily injected at frontend build time. If runtime configurability is needed, use a documented runtime config mechanism or build-time Compose args consistently; do not claim a runtime environment override works unless implemented and tested.

---

## 11. Requirements traceability

| Assignment section | Requirement | Epic(s) |
|---|---|---|
| §2.1 | FCFS admission, limited seats, temporary hold, confirm or release | EP-04–EP-07 |
| §2.2 | .NET 8+, PostgreSQL, React 18+, Docker, environment configurability | EP-01, EP-08, EP-11 |
| §2.3 | Create class, class list, admission detail, My Applications, no login, simulated payment | EP-02, EP-03, EP-06–EP-08 |
| §2.4 | Eight exact endpoints, JSON shapes and `X-Student-Id` | EP-02–EP-09 |
| §2.5 backend | No overselling, expiry, uniqueness, ownership, idempotency, persistence, 4xx input | EP-01, EP-04–EP-07, EP-09, EP-10 |
| §2.5 UI | UI-only flows, no-reload expiry and counts, refresh recovery, understandable errors | EP-03, EP-05, EP-08 |
| §2.6 bonus | Two backend processes sharing DB, frontend automated tests | EP-12 |
| §2.7 | Repo structure, Dockerfiles/Compose, README, ASSUMPTIONS, tests, AI_USAGE | EP-10, EP-11 |
| §3 | Public GitHub submission; do not redistribute assignment PDF | EP-11 |

---

## 12. Key assumptions, trade-offs and risks

| ID | Decision / risk | Mitigation or documentation |
|---|---|---|
| A-01 | Per-class PostgreSQL lock order is not strictly identical to the order of HTTP arrival. | State FCFS interpretation explicitly in `ASSUMPTIONS.md`; no lottery. |
| A-02 | A hold's effective `EXPIRED` status may be derived rather than persisted. | Use consistent expiry-aware calculations and projections in every endpoint. |
| A-03 | Local storage of a birth registration number is convenient but sensitive on a shared device. | Include Change/Clear ID; no URL exposure; note lack of production authentication. |
| A-04 | Open admin class creation is insecure in real deployment but is consistent with no-login task. | Label demo-only; do not add auth endpoints outside source contract. |
| A-05 | Confirm/cancel and duplicates have unspecified exact conflict codes. | Follow §6.2 and record mappings in `ASSUMPTIONS.md`. |
| A-06 | PostgreSQL `NOW()` reflects transaction start time and can be stale while waiting for a lock. | Obtain fresh `clock_timestamp()` after lock acquisition for mutation decisions. |
| A-07 | An expired stored `HELD` record cannot safely be represented by a time-varying partial unique index. | Enforce active uniqueness inside serialized class transaction, optionally unique-index confirmed state. |
| A-08 | Vite environment variables might be fixed at build time. | Test and document frontend API URL configuration in Docker. |
| A-09 | Thousands of requests contending for the same class can queue and increase latency. | Keep class transactions short; no external calls within lock; prioritize correctness over throughput. |
| A-10 | Exact behavior for reapplying after expired/cancelled hold is not specified. | Permit new hold if capacity available; retain history of older attempts. |
| A-11 | Cancellation after confirmation is not specified. | Return `409`, never release confirmed seat via DELETE hold endpoint. |
| A-12 | The assignment's original submission deadline has elapsed. | This PRD does not assert submission or an extension. |

---

## 13. Final release checklist

### Mandatory

- [ ] Eight exact API endpoints implemented.
- [ ] .NET 8+, PostgreSQL, React 18+, TypeScript and Docker work together.
- [ ] `GET /classes` and `GET /classes/{classId}` show correct, fresh, derived seat counts.
- [ ] Capacity-safe holds under concurrent load; no overselling.
- [ ] Correct `HOLD_DURATION_SECONDS` default and expiry behavior without cleanup worker.
- [ ] Per-student-per-class uniqueness and duplicate request recovery.
- [ ] Only owner's unexpired hold can become confirmed; repeat confirms are safe.
- [ ] Cancelled and expired seats are available to others; confirmed seats stay occupied.
- [ ] All four required UI workflows work without Postman.
- [ ] Seat counts auto-refresh every few seconds; expired status and disabled payment update without reload.
- [ ] Application history survives browser refresh/reopen; database data survives Compose down/up.
- [ ] Bad input gets 4xx and readable error UI, not an avoidable 500 or blank page.
- [ ] Three or more backend automated tests, including a concurrency test.
- [ ] Root Compose, Dockerfiles, README, ASSUMPTIONS.md, AI_USAGE.md and incremental commits.
- [ ] Public repository contains no secrets or confidential assignment PDF.

### Optional bonus

- [ ] Two backend processes sharing PostgreSQL pass the same no-overselling test.
- [ ] Frontend automated tests cover important state/error transitions.

---

## 14. Sign-off and change control

**Planning decisions:** Q1–Q8 were chosen in the question-by-question discussion (Q7 accepted via “OK”); Q9–Q22 use the assistant's recommendations at the user's explicit instruction.  
**PRD approval state:** Baseline documented for implementation; acceptance criteria still require proof from a running implementation.  
**Change rule:** Any adjustment to API success fields, seat-capacity invariants, student identity, expiration, or state transitions must be recorded in this PRD and `ASSUMPTIONS.md` and accompanied by updated tests. Mandatory assignment requirements cannot be changed by a local preference.

**End of PRD.**
