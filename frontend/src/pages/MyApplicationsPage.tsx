import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { listMyHolds } from "../api/holds";
import { useStudentId } from "../hooks/useStudentId";
import { queryKeys } from "../lib/queryKeys";
import { effectiveStatus, formatTimestamp } from "../lib/holdStatus";
import { EmptyState, ErrorState, LoadingState } from "../components/States";

export function MyApplicationsPage() {
  const { studentId } = useStudentId();

  const holdsQuery = useQuery({
    queryKey: queryKeys.myHolds(studentId ?? ""),
    queryFn: ({ signal }) => listMyHolds(studentId ?? "", signal),
    enabled: studentId !== null,
  });

  return (
    <main className="mx-auto max-w-3xl px-4 py-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-slate-900">My Applications</h1>
        <Link to="/" className="text-sm text-slate-700 underline">
          Back to classes
        </Link>
      </div>

      {studentId === null ? (
        <div className="mt-6">
          <EmptyState>
            Set your 17-digit student ID above to see your applications.
          </EmptyState>
        </div>
      ) : null}

      {studentId !== null ? (
        <div className="mt-6">
          {holdsQuery.isPending ? <LoadingState label="Loading your applications…" /> : null}

          {holdsQuery.isError ? (
            <ErrorState error={holdsQuery.error} onRetry={() => void holdsQuery.refetch()} />
          ) : null}

          {holdsQuery.isSuccess && holdsQuery.data.length === 0 ? (
            <EmptyState>
              You have no applications yet.{" "}
              <Link to="/" className="font-medium text-slate-900 underline">
                Browse classes
              </Link>
              .
            </EmptyState>
          ) : null}

          {holdsQuery.isSuccess && holdsQuery.data.length > 0 ? (
            <ul className="space-y-3">
              {holdsQuery.data.map((hold) => {
                const status = effectiveStatus(hold.status, hold.expiresAt);
                return (
                  <li key={hold.holdId}>
                    <Link
                      to={`/holds/${encodeURIComponent(hold.holdId)}`}
                      className="block rounded border border-slate-200 bg-white p-4 hover:border-slate-400"
                    >
                      <div className="flex flex-col gap-1 sm:flex-row sm:items-center sm:justify-between">
                        <span className="font-medium text-slate-900">{hold.className}</span>
                        <span className="text-xs font-semibold text-slate-700">{status}</span>
                      </div>
                      <p className="mt-1 text-sm text-slate-600">
                        Expires {formatTimestamp(hold.expiresAt)}
                      </p>
                    </Link>
                  </li>
                );
              })}
            </ul>
          ) : null}
        </div>
      ) : null}
    </main>
  );
}
