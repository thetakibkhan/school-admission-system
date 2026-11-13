import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { listClasses } from "../api/classes";
import { createHold } from "../api/holds";
import { ApiError } from "../api/client";
import type { ClassResponse } from "../api/types";
import { useStudentId } from "../hooks/useStudentId";
import { queryKeys } from "../lib/queryKeys";
import { EmptyState, ErrorState, LoadingState } from "../components/States";

const POLL_INTERVAL_MS = 3000;

export function ClassesPage() {
  const { studentId } = useStudentId();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [applyError, setApplyError] = useState<unknown>(null);

  const classesQuery = useQuery({
    queryKey: queryKeys.classes,
    queryFn: ({ signal }) => listClasses(signal),
    refetchInterval: POLL_INTERVAL_MS,
  });

  const applyMutation = useMutation({
    mutationFn: (classId: string) => {
      if (studentId === null) {
        throw new Error("A student ID is required before applying.");
      }
      return createHold(classId, studentId);
    },
    onSuccess: (hold) => {
      setApplyError(null);
      void queryClient.invalidateQueries({ queryKey: queryKeys.classes });
      navigate(`/holds/${encodeURIComponent(hold.holdId)}`);
    },
    onError: (error) => {
      setApplyError(error);
      // The server is authoritative: a 409 means our count was stale, so refetch.
      if (error instanceof ApiError && error.status === 409) {
        void queryClient.invalidateQueries({ queryKey: queryKeys.classes });
      }
    },
  });

  const canApply = studentId !== null;

  return (
    <main className="mx-auto max-w-4xl px-4 py-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <h1 className="text-2xl font-semibold text-slate-900">Classes</h1>
        <nav className="flex gap-2" aria-label="Main">
          <Link
            to="/classes/new"
            className="rounded bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
          >
            Create Class
          </Link>
          <Link
            to="/me"
            className="rounded border border-slate-300 px-3 py-1.5 text-sm hover:bg-slate-100"
          >
            My Applications
          </Link>
        </nav>
      </div>

      {!canApply ? (
        <p className="mt-4 rounded border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
          Set your 17-digit student ID above to apply for a class.
        </p>
      ) : null}

      {applyError !== null ? (
        <div className="mt-4">
          <ErrorState
            error={applyError}
            onRetry={() => {
              const classId = applyMutation.variables;
              if (classId !== undefined) {
                applyMutation.mutate(classId);
              }
            }}
          />
        </div>
      ) : null}

      <div className="mt-6">
        {classesQuery.isPending ? <LoadingState label="Loading classes…" /> : null}

        {classesQuery.isError ? (
          <ErrorState error={classesQuery.error} onRetry={() => void classesQuery.refetch()} />
        ) : null}

        {classesQuery.isSuccess && classesQuery.data.length === 0 ? (
          <EmptyState>
            No classes yet.{" "}
            <Link to="/classes/new" className="font-medium text-slate-900 underline">
              Create the first class
            </Link>
            .
          </EmptyState>
        ) : null}

        {classesQuery.isSuccess && classesQuery.data.length > 0 ? (
          <ClassTable
            classes={classesQuery.data}
            canApply={canApply}
            pendingClassId={
              applyMutation.isPending ? (applyMutation.variables ?? null) : null
            }
            onApply={(classId) => applyMutation.mutate(classId)}
          />
        ) : null}
      </div>
    </main>
  );
}

function ClassTable({
  classes,
  canApply,
  pendingClassId,
  onApply,
}: {
  classes: ClassResponse[];
  canApply: boolean;
  pendingClassId: string | null;
  onApply: (classId: string) => void;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-left text-sm">
        <caption className="sr-only">Classes and seat availability, refreshed every 3 seconds</caption>
        <thead>
          <tr className="border-b border-slate-300 text-slate-600">
            <th scope="col" className="py-2 pr-3 font-medium">Class</th>
            <th scope="col" className="py-2 pr-3 font-medium">Total</th>
            <th scope="col" className="py-2 pr-3 font-medium">Held</th>
            <th scope="col" className="py-2 pr-3 font-medium">Confirmed</th>
            <th scope="col" className="py-2 pr-3 font-medium">Available</th>
            <th scope="col" className="py-2 font-medium">Action</th>
          </tr>
        </thead>
        <tbody>
          {classes.map((item) => {
            const soldOut = item.availableSeats === 0;
            const disabled = soldOut || !canApply || pendingClassId === item.id;
            return (
              <tr key={item.id} className="border-b border-slate-200">
                <td className="py-2 pr-3 font-medium text-slate-900">{item.name}</td>
                <td className="py-2 pr-3">{item.totalSeats}</td>
                <td className="py-2 pr-3">{item.heldSeats}</td>
                <td className="py-2 pr-3">{item.confirmedSeats}</td>
                <td className="py-2 pr-3">
                  <span className={soldOut ? "font-semibold text-red-700" : "font-semibold text-emerald-700"}>
                    {item.availableSeats}
                  </span>
                </td>
                <td className="py-2">
                  <button
                    type="button"
                    onClick={() => onApply(item.id)}
                    disabled={disabled}
                    aria-label={`Apply for ${item.name}`}
                    className="rounded bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700 disabled:cursor-not-allowed disabled:bg-slate-300"
                  >
                    {soldOut ? "Full" : pendingClassId === item.id ? "Applying…" : "Apply"}
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
