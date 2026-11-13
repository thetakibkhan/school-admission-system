# AI usage

This file records which AI tools were used, what they did, the real mistakes that were caught, and how
the result was verified. It is written to be checkable against the repository's own artefacts, not to
be flattering.

## Tools used

| Tool | Used for |
|---|---|
| Claude Code (Anthropic), running as a multi-agent orchestrator inside Herdr | Planning the work, splitting it into tasks, writing each task's contract, running the review loop, and integrating the branches. |
| Claude Code implementer agents | One agent per task: foundation (solution, schema, migration, Compose, Dockerfiles), classes (class endpoints), holds (reservation, confirm, cancel, expiry, history), frontend (React app), concurrency (the 50-request test), delivery (this documentation and the Compose smoke proof). |
| Claude Code reviewer agent | Reviewed every branch head against the task contract and the PRD, reporting only P0 and P1 findings, with a second pass after fixes. |
| Claude (chat) | The original product discussion that produced the PRD: the 22 design decisions (Q1–Q22), the epic backlog and the acceptance criteria. |

The PRD in this repository (`PRD.md`) was produced in that planning conversation and is the contract
every implementation task was written against. The code was written by the implementer agents; the
reviewer agent read it independently and did not write it.

## A real error, and how it was caught

The foundation branch shipped `frontend/Dockerfile` with a plain `RUN npm ci`. The reviewer flagged it
as a P1 before any frontend code existed:

> `frontend/Dockerfile:9` — `npm ci` needs a package-lock.json, but the frontend task's contract allows
> shipping only package.json (`npm install` when no lock file exists), so the web image build fails and
> Must-prove 1 breaks.

The delivery task then reproduced it for real. `docker compose up --build -d` failed at the `web`
service with:

```text
#29 0.535 npm error The `npm ci` command can only install with an existing package-lock.json or
#29 0.535 npm error npm-shrinkwrap.json with lockfileVersion >= 1.
target web: failed to solve: process "/bin/sh -c npm ci" did not complete successfully: exit code: 1
```

The same review found a second, quieter defect in the same file: `nginx:alpine`'s default config has no
SPA fallback, so a browser refresh on any React Router path would return `404` — which would have
broken the "My Applications restored after a browser restart" requirement even after the build was
fixed.

Both are fixed in commit `3c80da4` (a lockfile-aware install, and an `nginx.conf` with
`try_files $uri $uri/ /index.html`); the shipped `frontend/Dockerfile` and `frontend/nginx.conf` are
the result.

A second real error was caught during the build work:

- An orphaned `VBCSCompiler` process outlived a build and kept the machine-wide check lock, so later
  checks waited forever. Caught by reading the lock holder with `ps`; fixed by adding
  `-p:UseSharedCompilation=false -nodeReuse:false` to every build and test command.

## How the result was verified

This section records what was verified, and how a reader can check it. Anything not verified is named
as such.

- **Business rules.** The PRD's state-transition table (section 5.3) and error-code table (section 6.2)
  were written by the human in the planning conversation, before any code. The implementation was
  checked against those tables rather than against its own description — that is how the `npm ci`
  defect in `frontend/Dockerfile` was found. The assumptions the PRD left open are written down in
  `ASSUMPTIONS.md` so a reader can disagree with a specific choice instead of guessing at it.
- **The Docker path, database and API.** `docker compose up --build` builds all three images and starts
  `db`, `api` and `web`; `GET /classes` answers `200` JSON through the published API port, the web
  container serves the SPA and its deep links, and a class created before `docker compose down` is
  still returned by `GET /classes` after `docker compose up` with no `-v`. The earlier partial run (db
  and api only, with the `web` image failing at `RUN npm ci` before the frontend source existed) is
  what the `npm ci` error above describes; the shipped `frontend/Dockerfile` closes that gap.
- **The concurrency proof is in the tree and passes.** The 50-request overselling test is a Must-prove
  item and lives in `backend.Tests/ConcurrencyTests.cs`. The documented command runs it: 5 concurrency
  tests pass against real PostgreSQL, and the overselling test fires 50 simultaneous distinct-student
  hold requests at a 10-seat class and asserts `heldSeats + confirmedSeats <= totalSeats` afterwards.
  The human's check is to run the documented command, read the pass count, and confirm the test fails
  when the class row lock is removed. That reproduction was run: with the lock removed, `NoOverselling`
  fails with `Expected: 10 / Actual: 50`; with the lock restored, it passes. The lock is therefore
  load-bearing, not decorative.
- **The UI workflows were verified in a browser.** Against the containerised app at
  `http://localhost:5173`, all four required workflows were walked: a class was created through
  `/classes/new` and appeared in the list; the list polled every 3 seconds; a seat was reserved and the
  hold page showed a live countdown with Pay and Cancel enabled; confirming moved the hold to
  `CONFIRMED` and the class list to Confirmed 1 / Available 9; `/me` listed the confirmed row and the
  student ID was restored from `localStorage` after a full page reload. With a short hold duration, a
  hold reached `EXPIRED` on its own with no reload and no interaction, the seat was released, and a
  confirm attempt then returned `409`. CORS was confirmed from the browser's network panel: the
  preflight and the cross-origin POST both carried `access-control-allow-origin: http://localhost:5173`.
- **The backend tests were run.** 79 tests pass against real PostgreSQL through the documented command:
  Foundation 19, Classes 16, Holds 24, Concurrency 5, Contract 9, Expiry 4, Persistence 2. The
  concurrency test is included in that run.

## Secrets and personal data

No API keys, tokens, passwords or prompts containing secrets are included in this repository. The only
credentials in the tree are the local demo database values in `.env.example`
(`POSTGRES_PASSWORD=postgres`), which are the defaults of a throwaway container and are documented as
such. No real student data is present: every student ID in the tests and examples is a synthetic
17-digit number, and the database starts empty. The confidential assignment PDF is not in the
repository or its history.
