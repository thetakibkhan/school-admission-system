# Assumptions

The assignment PDF fixes the eight endpoints, the JSON field names, the `409` for a full class and the
`X-Student-Id` header. Everything else in this file is a gap in the source that had to be filled to
build a working system. Each section names the PRD reference and the behaviour this implementation
chose. Where the PRD already recorded the decision, the section says so and adds the concrete mapping.

## 1. A duplicate hold request returns `200` with the original expiry

**PRD 6 note 1, Q6, invariant I-05.**

A student who already has an active `HELD` row for a class and applies again gets `200 OK` with the
same `holdId`, the same `classId`, the same `studentId`, the same `status` and the **same `expiresAt`**.
No row is inserted and the expiry is never extended. A first-time hold is `201 Created`.

The check runs inside the class row lock, so two simultaneous duplicate requests cannot both insert.
The lookup is `status = 'HELD' AND expires_at > db_now`; an expired hold is not "active" and a new hold
is created instead (see section 4).

## 2. Class names are unique case-insensitively

**PRD 5.1, `classes.name`.**

`name` is trimmed and stored as entered. A second column, `normalized_name`, holds
`name.Trim().ToLowerInvariant()` and carries a unique index (`ux_classes_normalized_name`). A duplicate
returns `409 Conflict` with a Problem Details body, never a `500`. This is correct for the single-school
demo the assignment describes; a multi-school system would scope the uniqueness to a school.

`total_seats` must be a positive integer (`> 0`) and is immutable after creation, because the
assignment specifies no class-update endpoint. The database enforces `total_seats > 0` with a check
constraint as well as the API validating it.

## 3. Error-code mapping

**PRD 6.2.** The PRD fixes only the no-seats `409`. Every other row below is the mapping this
implementation uses. All errors are `application/problem+json` with `type`, `title`, `status` and
`detail`; no stack trace, no connection string and no other student's ID ever appears in a response.

| Situation | Code | `detail` intent |
|---|---|---|
| `X-Student-Id` missing, or not exactly 17 ASCII digits | `400` | Enter exactly 17 digits. |
| `name` null, empty or whitespace-only | `400` | The detail names the `name` field. |
| `totalSeats` null, `<= 0`, or greater than 100000 | `400` | The detail names the `totalSeats` field. |
| Malformed JSON body | `400` | Correct the request body. |
| Unknown `classId` on `GET /classes/{id}` or on hold create | `404` | Class not found. |
| Unknown `holdId` on get, confirm or delete | `404` | Application not found. |
| Wrong student on confirm or delete | `404` | Application not found for this student. |
| No seats remaining | `409` | All seats are currently occupied. |
| Student already has a `CONFIRMED` admission in this class | `409` | Already admitted to this class. |
| Confirming an expired or cancelled hold | `409` | Reservation can no longer be confirmed. |
| Cancelling a confirmed admission | `409` | Confirmed admission cannot be cancelled here. |
| Duplicate class name (case-insensitive) | `409` | A class with this name already exists. |
| Unexpected server error | `500` | A temporary problem occurred. Please retry. |
| Database unavailable, timeout, or a write conflict | `503` | A temporary problem occurred. Please retry. |

The `500`/`503` split is made by the global exception handler: `DbUpdateException`, `NpgsqlException`
and `TimeoutException` map to `503`, everything else to `500`. Both use a fixed detail string.

## 4. `EXPIRED` is derived, never stored

**PRD 5.1, Q12, invariant I-04.**

The `holds.status` column stores only `HELD`, `CONFIRMED` and `CANCELLED`, enforced by a check
constraint. A hold is **effectively** expired when `status = 'HELD' AND expires_at <= db_now`, where
`db_now` is PostgreSQL `clock_timestamp()`. Every read and every decision derives the status the same
way, so an expired hold stops consuming capacity with no request and no worker.

The exact boundary is `db_now < expires_at` for "still active". At `db_now >= expires_at` the hold is
expired. A `CONFIRMED` admission keeps its seat after its original `expiresAt` (invariant I-03).

No cleanup job exists and none is needed. A later opportunistic rewrite of stored `HELD` rows to
`EXPIRED` would be an optimisation only, and nothing may depend on it.

## 5. Cancellation semantics

**PRD 5.3, Q14.**

| Current effective state | `DELETE /holds/{id}` by the owning student |
|---|---|
| `HELD` before expiry | `204`, row becomes `CANCELLED`, seat released immediately |
| `CANCELLED` | `204`, no change (safe to retry) |
| `EXPIRED` | `204`, no change (the seat was already released by time) |
| `CONFIRMED` | `409`, the seat stays occupied |

A confirmed admission is never released through the hold endpoint. Releasing it would need a separate
administrative action that the assignment does not specify.

## 6. A wrong student gets `404`, not `403`

**PRD 5.6.**

Confirm and delete are allowed only for the student in `X-Student-Id`. A mismatch returns
`404 Not Found` with the same body as an unknown hold, so the response does not reveal that the hold
exists or who owns it. `403` would confirm the record's existence to a stranger.

## 7. `GET /holds/{holdId}` requires no `X-Student-Id`

**PRD 5.6, PRD 6 table row 5.**

The assignment's example for this route shows no identity header, so the route stays compatible with
it: no header is required and none is checked. The response carries only `holdId`, `classId`, `status`
and `expiresAt` — no student ID and no class name. In a production system this route would need
authorization; here it is a read-only status lookup by an unguessable ID.

The four routes that do require the header are `POST /classes/{classId}/holds`,
`POST /holds/{holdId}/confirm`, `DELETE /holds/{holdId}` and `GET /me/holds`. A missing or malformed
header on any of them returns `400` before any database work.

## 8. Lock order is not HTTP arrival order

**PRD 5.5, A-01.**

Admission is first-come-first-served in the sense that there is no lottery and no randomisation.
The effective order is the order in which requests acquire the PostgreSQL class row lock
(`SELECT … FOR UPDATE`). That is a real serialised order, and it is the only order a single database
can define without an intake queue.

It is **not** a guarantee that the first request the web server received wins. Two requests that
arrive microseconds apart can acquire the lock in the other order, and a request that waited on a
connection pool can lose to a later one. Strict arrival-order FIFO would require an intake sequence or
a queue in front of the database, which the assignment does not ask for.

## 9. No authentication, simulated payment, open admin action

**PRD 1.2, 1.3, Q21, A-03, A-04.**

- There is no login, no password, no OTP and no role model. `X-Student-Id` is a claim the client makes,
  not a credential. Anyone who knows a 17-digit number can act as that student.
- "Payment" is a single `POST /holds/{id}/confirm` call. There is no gateway, no invoice, no refund and
  no webhook.
- Class creation is an open action with no authorization. That matches the assignment's no-login
  requirement and is labelled demo-only in the UI and the README.
- The browser stores the student ID in `localStorage` for convenience, with Change and Clear controls.
  It is never put in a URL and never logged. On a shared device that storage is a privacy risk, which
  is why the Clear control exists.

This system must not be described as a production-secure admission service.

## 10. `HOLD_DURATION_SECONDS` is read at startup

**PRD 10.4, R10.**

The value is read once when the API starts and held in a singleton. A change needs a container
restart. A value that is present but not a positive integer aborts startup with a logged error and a
non-zero exit, rather than silently falling back to the default. An absent value means `600`.

`expiresAt` is computed as `db_now + HOLD_DURATION_SECONDS` where `db_now` is PostgreSQL
`clock_timestamp()` read **after** the class row lock is acquired, so a request that waited on the lock
does not get a hold that is already partly expired.

## 11. Reapplying after an expired or cancelled hold

**PRD 5.3, A-10.**

An expired or cancelled hold does not block a new application. If seats remain, a new `HELD` row is
created with a new ID and a new expiry; the old row is kept as history and appears in `GET /me/holds`
with its own status. A student who already holds a `CONFIRMED` admission in the class gets `409`
instead, because a second confirmed admission would double-count the seat.

## 12. Seat counts are derived, never stored

**PRD 5.2, Q3, R7.**

`classes` has no `held_seats` or `confirmed_seats` column. Every read computes:

```text
heldSeats      = COUNT(holds WHERE class_id = C AND status = 'HELD' AND expires_at > db_now)
confirmedSeats = COUNT(holds WHERE class_id = C AND status = 'CONFIRMED')
availableSeats = totalSeats - heldSeats - confirmedSeats
```

All three come from one database-time reading per request, so a list response cannot mix two clock
values. A mutable counter would need to be decremented by a worker at expiry, which is exactly the
design the PRD rejects.

## 13. Student IDs are text, never numbers

**PRD 5.6, R4.**

A student ID is exactly 17 ASCII digits (`^[0-9]{17}$`), stored in a `text` column, sent as a JSON
string and validated with a character loop rather than a numeric parse. Leading zeros are preserved.
The database enforces the format with a check constraint (`ck_holds_student_id_format`) as a second
line of defence behind the API validation. A 16-digit or 18-digit value is rejected with `400`; it is
never trimmed or padded.

## 14. Timestamps

**PRD 6, R5.**

Every timestamp leaves the API as ISO 8601 UTC with a `Z` suffix, for example
`2026-11-01T04:10:00Z`. Columns are `timestamp with time zone`. No decision path uses
`DateTime.UtcNow`, `DateTimeOffset.Now` or a local time zone; the only clock that decides anything is
PostgreSQL's. The browser countdown is display-only and the server's answer always wins.

## 15. Test fixtures and seeded data

**PRD EP-11 / US-11.2.**

There is no seed data and no fixture file. The API starts with an empty database and the UI shows an
empty state until a class is created. The backend tests create their own rows through the real HTTP
API or with direct SQL inside the test, and they drop and recreate the schema from the real migrations
at startup. Nothing depends on an external service beyond the PostgreSQL instance named by
`TEST_DATABASE_URL`.
