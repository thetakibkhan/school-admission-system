# AI usage

This file records which AI tools were used, what they did, one real mistake that was caught, and how
the result was verified by a human. It is written to be checkable against the repository's own
artefacts, not to be flattering.

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

The delivery task then reproduced it for real. `docker compose up --build -d` on the combined tree
failed at the `web` service with:

```text
#29 0.535 npm error The `npm ci` command can only install with an existing package-lock.json or
#29 0.535 npm error npm-shrinkwrap.json with lockfileVersion >= 1.
target web: failed to solve: process "/bin/sh -c npm ci" did not complete successfully: exit code: 1
```

The same review found a second, quieter defect in the same file: `nginx:alpine`'s default config has no
SPA fallback, so a browser refresh on any React Router path would return `404` — which would have
broken the "My Applications restored after a browser restart" requirement even after the build was
fixed.

Both are recorded in `.herdsman/school-admission-2026-11-13/reports/review-foundation-1.md` and are
fixed by the integrator in `frontend/Dockerfile` (a lockfile-aware install, and an `nginx.conf` with
`try_files $uri $uri/ /index.html`). The delivery task did not edit the Dockerfile, because that file
belongs to the foundation task.

Two smaller real errors, both recorded in the route's reports:

- An orphaned `VBCSCompiler` process outlived a build and kept the machine-wide check lock, so later
  checks waited forever. Caught by reading the lock holder with `ps`; fixed by adding
  `-p:UseSharedCompilation=false -nodeReuse:false` to every build and test command.
- xUnit's default class-level parallelism let one test class drop the shared test schema while another
  class was mid-request, producing spurious `503`s. Caught by the classes task when its own tests
  failed intermittently; fixed with `DisableTestParallelization` in the test assembly.

## How the result was verified by a human

The human's role was to set the contract, review the evidence, and run the acceptance path — not to
take the agents' word for it.

- **Business rules.** The PRD's state-transition table (section 5.3) and error-code table (section 6.2)
  were written by the human in the planning conversation, before any code. Every implementer task was
  given those tables as its contract, and the reviewer checked the code against them rather than
  against the implementer's own description. The assumptions the PRD left open are written down in
  `ASSUMPTIONS.md` so a reader can disagree with a specific choice instead of guessing at it.
- **Concurrency.** The concurrency test is not a mock and not an in-memory provider: it starts the real
  API over the real HTTP stack against a real PostgreSQL database, fires 50 simultaneous distinct-student
  hold requests at a 10-seat class, and asserts `heldSeats + confirmedSeats <= totalSeats` afterwards.
  It was required to fail when the class row lock is removed, so a passing run means the lock is doing
  the work. The human ran the documented test command and read the pass count.
- **The Docker path.** The human ran `docker compose up --build` from a clean tree, called the API with
  `curl`, and ran `docker compose down` followed by `docker compose up` to confirm the data survived.
  The exact commands and their real output are in the delivery report.
- **The UI.** The four required workflows were walked through in a browser with no Postman and no
  database edits: create a class, watch the class list poll, reserve and confirm or cancel with the
  countdown, and reopen the browser to see My Applications restored from `localStorage`.

## Secrets and personal data

No API keys, tokens, passwords or prompts containing secrets are included in this repository. The only
credentials in the tree are the local demo database values in `.env.example`
(`POSTGRES_PASSWORD=postgres`), which are the defaults of a throwaway container and are documented as
such. No real student data is present: every student ID in the tests and examples is a synthetic
17-digit number, and the database starts empty. The confidential assignment PDF is not in the
repository or its history.
