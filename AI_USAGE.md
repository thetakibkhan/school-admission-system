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

The delivery task then reproduced it for real. `docker compose up --build -d` on this branch failed at
the `web` service with:

```text
#29 0.535 npm error The `npm ci` command can only install with an existing package-lock.json or
#29 0.535 npm error npm-shrinkwrap.json with lockfileVersion >= 1.
target web: failed to solve: process "/bin/sh -c npm ci" did not complete successfully: exit code: 1
```

The same review found a second, quieter defect in the same file: `nginx:alpine`'s default config has no
SPA fallback, so a browser refresh on any React Router path would return `404` — which would have
broken the "My Applications restored after a browser restart" requirement even after the build was
fixed.

Both are recorded in `.herdsman/school-admission-2026-11-13/reports/review-foundation-1.md`; the
foundation task fixed them in commit `3c80da4` (a lockfile-aware install, and an `nginx.conf` with
`try_files $uri $uri/ /index.html`).

A second real error is recorded in the foundation task's own report:

- An orphaned `VBCSCompiler` process outlived a build and kept the machine-wide check lock, so later
  checks waited forever. Caught by reading the lock holder with `ps`; fixed by adding
  `-p:UseSharedCompilation=false -nodeReuse:false` to every build and test command.

## How the result was verified

This section records only what the route's reports actually show, plus the checks run directly by the
delivery task. Anything not yet verified is named as pending.

- **Business rules.** The PRD's state-transition table (section 5.3) and error-code table (section 6.2)
  were written by the human in the planning conversation, before any code. Every implementer task was
  given those tables as its contract, and the reviewer agent checked the code against them rather than
  against the implementer's own description — that is how the `npm ci` defect in `frontend/Dockerfile`
  was found. The assumptions the PRD left open are written down in `ASSUMPTIONS.md` so a reader can
  disagree with a specific choice instead of guessing at it.
- **The Docker path, database and API.** Verified on this branch for the services that exist: the
  delivery task ran `docker compose up --build -d db api` (exit 0), waited for `/health`
  (`200 {"status":"ok"}`), then ran `docker compose down` and `docker compose up -d` and confirmed a row
  written to PostgreSQL before the shutdown was still present afterwards. The exact commands and output
  are in `.herdsman/school-admission-2026-11-13/reports/delivery.md`. The full `docker compose up
  --build` including the `web` service **did not pass** on this branch: the frontend source is not here
  yet, and the build stops at `RUN npm ci` because there is no `package.json`. It is recorded under
  `gaps:`, not claimed as a pass.
- **The concurrency proof is pending.** The 50-request overselling test is a Must-prove item written by
  the concurrency task. No branch in this route contains it yet, and no run of it has been recorded.
  `README.md` marks it as required and pending. When it lands, the human's check is to run the
  documented command, read the pass count, and confirm the test fails when the class row lock is
  removed.
- **The UI walkthrough is pending.** The four required workflows — create a class, watch the list poll,
  reserve/confirm/cancel with the countdown, and reopen the browser to see My Applications restored
  from `localStorage` — have not been walked in a browser and no route report records a browser
  session. They depend on the frontend branch and on a full `docker compose up --build`, neither of
  which is present on this branch.
- **The backend tests that do exist were run.** 18 foundation tests passed against real PostgreSQL
  through the documented command, and the classes task reports 34/34 on its branch.

## Secrets and personal data

No API keys, tokens, passwords or prompts containing secrets are included in this repository. The only
credentials in the tree are the local demo database values in `.env.example`
(`POSTGRES_PASSWORD=postgres`), which are the defaults of a throwaway container and are documented as
such. No real student data is present: every student ID in the tests and examples is a synthetic
17-digit number, and the database starts empty. The confidential assignment PDF is not in the
repository or its history.
