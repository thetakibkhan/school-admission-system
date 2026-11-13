import { useState } from "react";
import type { FormEvent } from "react";
import { isValidStudentId, maskStudentId, useStudentId } from "../hooks/useStudentId";

/**
 * Persistent identity field shown near the top of every page. The active id is
 * masked to its last four digits; Change and Clear are always available.
 */
export function StudentIdBar() {
  const { studentId, setStudentId, clearStudentId } = useStudentId();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");
  const [error, setError] = useState<string | null>(null);

  function startEditing() {
    setDraft("");
    setError(null);
    setEditing(true);
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = draft.trim();
    if (!isValidStudentId(trimmed)) {
      setError("Enter exactly 17 digits.");
      return;
    }
    setStudentId(trimmed);
    setEditing(false);
    setError(null);
  }

  return (
    <section
      aria-label="Student identity"
      className="border-b border-slate-200 bg-white px-4 py-3"
    >
      <div className="mx-auto flex max-w-4xl flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-sm text-slate-700">
          <span className="font-semibold">Student ID:</span>{" "}
          {studentId === null ? (
            <span className="text-slate-500">not set</span>
          ) : (
            <span data-testid="active-student-id" className="font-mono">
              {maskStudentId(studentId)}
            </span>
          )}
        </p>

        {editing ? (
          <form onSubmit={handleSubmit} className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <label htmlFor="student-id-input" className="sr-only">
              Student ID (17 digits)
            </label>
            <input
              id="student-id-input"
              name="studentId"
              inputMode="numeric"
              autoComplete="off"
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              placeholder="17-digit student ID"
              aria-invalid={error !== null}
              aria-describedby={error !== null ? "student-id-error" : undefined}
              className="w-full rounded border border-slate-300 px-3 py-1.5 text-sm sm:w-56"
            />
            <div className="flex gap-2">
              <button
                type="submit"
                className="rounded bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
              >
                Save
              </button>
              <button
                type="button"
                onClick={() => {
                  setEditing(false);
                  setError(null);
                }}
                className="rounded border border-slate-300 px-3 py-1.5 text-sm hover:bg-slate-100"
              >
                Cancel
              </button>
            </div>
            {error !== null ? (
              <p id="student-id-error" role="alert" className="text-sm text-red-700">
                {error}
              </p>
            ) : null}
          </form>
        ) : (
          <div className="flex gap-2">
            <button
              type="button"
              onClick={startEditing}
              className="rounded border border-slate-300 px-3 py-1.5 text-sm hover:bg-slate-100"
            >
              {studentId === null ? "Set ID" : "Change"}
            </button>
            {studentId !== null ? (
              <button
                type="button"
                onClick={clearStudentId}
                className="rounded border border-slate-300 px-3 py-1.5 text-sm hover:bg-slate-100"
              >
                Clear
              </button>
            ) : null}
          </div>
        )}
      </div>
    </section>
  );
}
