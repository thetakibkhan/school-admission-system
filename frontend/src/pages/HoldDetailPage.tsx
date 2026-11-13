import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getClass } from "../api/classes";
import { cancelHold, confirmHold, getHoldWithMeta } from "../api/holds";
import { useStudentId } from "../hooks/useStudentId";
import { queryKeys } from "../lib/queryKeys";
import {
  formatCountdown,
  formatTimestamp,
  remainingMs,
  serverNow,
} from "../lib/holdStatus";
import { ErrorState, InlineError, LoadingState } from "../components/States";

const TICK_MS = 1000;

export function HoldDetailPage() {
  const { holdId = "" } = useParams<{ holdId: string }>();
  const { studentId } = useStudentId();
  const queryClient = useQueryClient();
  const [localNowMs, setLocalNowMs] = useState(() => Date.now());

  const holdQuery = useQuery({
    queryKey: queryKeys.hold(holdId),
    queryFn: ({ signal }) => getHoldWithMeta(holdId, signal),
    enabled: holdId.length > 0,
  });

  const payload = holdQuery.data?.data;
  const meta = holdQuery.data?.meta;

  const classId = payload?.classId;
  const classQuery = useQuery({
    queryKey: queryKeys.class(classId ?? ""),
    queryFn: ({ signal }) => getClass(classId ?? "", signal),
    enabled: classId !== undefined && classId.length > 0,
  });

  // The server's status is authoritative. Only a HELD answer is refined locally, and
  // the refinement runs on the server's clock (Date header + elapsed), never on the
  // browser clock, so a skewed browser cannot hide Pay/Cancel for a live hold. The
  // refinement is never pinned: once the server-clock countdown reaches zero the hold
  // is EXPIRED again, so a stale HELD answer cannot keep the actions alive forever.
  const status = useMemo(() => {
    if (payload === undefined) {
      return undefined;
    }
    if (payload.status !== "HELD" || meta === undefined) {
      return payload.status;
    }
    return serverNow(meta, localNowMs) < Date.parse(payload.expiresAt) ? "HELD" : "EXPIRED";
  }, [payload, meta, localNowMs]);

  // Tick once per second while the hold is still effectively HELD.
  useEffect(() => {
    if (status !== "HELD") {
      return;
    }
    const timer = window.setInterval(() => setLocalNowMs(Date.now()), TICK_MS);
    return () => window.clearInterval(timer);
  }, [status]);

  // When our server-clock countdown reaches zero but the server still says HELD, ask
  // once and let the server's answer win. The flag resets whenever the hold is HELD
  // again, so a fresh HELD answer gets a fresh refetch at the next zero, while a
  // server that keeps answering HELD cannot make this spin.
  const refetchHold = holdQuery.refetch;
  const expiryRefetched = useRef(false);
  useEffect(() => {
    if (status === "HELD") {
      expiryRefetched.current = false;
      return;
    }
    if (status !== "EXPIRED" || payload === undefined || meta === undefined) {
      return;
    }
    if (payload.status !== "HELD" || !meta.hasServerClock || expiryRefetched.current) {
      return;
    }
    expiryRefetched.current = true;
    void refetchHold();
  }, [status, payload, meta, refetchHold]);

  // A different hold starts from a clean slate.
  useEffect(() => {
    expiryRefetched.current = false;
  }, [holdId]);

  const confirmMutation = useMutation({
    mutationFn: () => {
      if (studentId === null) {
        throw new Error("A student ID is required.");
      }
      return confirmHold(holdId, studentId);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.hold(holdId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.classes });
      if (studentId !== null) {
        void queryClient.invalidateQueries({ queryKey: queryKeys.myHolds(studentId) });
      }
    },
  });

  const cancelMutation = useMutation({
    mutationFn: () => {
      if (studentId === null) {
        throw new Error("A student ID is required.");
      }
      return cancelHold(holdId, studentId);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.hold(holdId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.classes });
      if (studentId !== null) {
        void queryClient.invalidateQueries({ queryKey: queryKeys.myHolds(studentId) });
      }
    },
  });

  if (holdQuery.isPending) {
    return (
      <main className="mx-auto max-w-2xl px-4 py-6">
        <LoadingState label="Loading application…" />
      </main>
    );
  }

  if (holdQuery.isError) {
    return (
      <main className="mx-auto max-w-2xl px-4 py-6">
        <ErrorState error={holdQuery.error} onRetry={() => void holdQuery.refetch()} />
        <p className="mt-4 text-sm">
          <Link to="/" className="underline">
            Back to classes
          </Link>
        </p>
      </main>
    );
  }

  if (payload === undefined || meta === undefined) {
    return (
      <main className="mx-auto max-w-2xl px-4 py-6">
        <LoadingState label="Loading application…" />
      </main>
    );
  }

  const hold = payload;
  const className = classQuery.data?.name ?? "Class";
  const isHeld = status === "HELD";
  const actionError = confirmMutation.error ?? cancelMutation.error;
  // Count down on the server clock so a skewed browser clock cannot mislead.
  const countdownMs = remainingMs(hold.expiresAt, serverNow(meta, localNowMs));

  return (
    <main className="mx-auto max-w-2xl px-4 py-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-slate-900">Application</h1>
        <Link to="/" className="text-sm text-slate-700 underline">
          Back to classes
        </Link>
      </div>

      <dl className="mt-6 space-y-3 rounded border border-slate-200 bg-white p-4 text-sm">
        <div className="flex justify-between gap-4">
          <dt className="font-medium text-slate-600">Class</dt>
          <dd className="text-right text-slate-900">{className}</dd>
        </div>
        <div className="flex justify-between gap-4">
          <dt className="font-medium text-slate-600">Status</dt>
          <dd>
            <StatusBadge status={status ?? hold.status} />
          </dd>
        </div>
        <div className="flex justify-between gap-4">
          <dt className="font-medium text-slate-600">Expires at</dt>
          <dd className="text-right text-slate-900">{formatTimestamp(hold.expiresAt)}</dd>
        </div>
        {isHeld ? (
          <div className="flex justify-between gap-4">
            <dt className="font-medium text-slate-600">Time remaining</dt>
            <dd className="text-right font-mono text-slate-900" data-testid="countdown">
              {formatCountdown(countdownMs)}
            </dd>
          </div>
        ) : null}
      </dl>

      {status === "EXPIRED" ? (
        <div className="mt-4 rounded border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">
          <p className="font-semibold">This reservation has expired.</p>
          <p className="mt-1">
            The seat was released. If seats are still available you can apply again from the{" "}
            <Link to="/" className="font-medium underline">
              class list
            </Link>
            .
          </p>
        </div>
      ) : null}

      {status === "CONFIRMED" ? (
        <div className="mt-4 rounded border border-emerald-300 bg-emerald-50 p-4 text-sm text-emerald-900">
          <p className="font-semibold">Admission confirmed.</p>
          <p className="mt-1">This seat is yours and cannot be cancelled here.</p>
        </div>
      ) : null}

      {status === "CANCELLED" ? (
        <div className="mt-4 rounded border border-slate-300 bg-slate-50 p-4 text-sm text-slate-700">
          <p className="font-semibold">This application was cancelled.</p>
          <p className="mt-1">
            You can apply again from the{" "}
            <Link to="/" className="font-medium underline">
              class list
            </Link>
            .
          </p>
        </div>
      ) : null}

      {actionError !== null ? (
        <div className="mt-4">
          <InlineError error={actionError} />
        </div>
      ) : null}

      {isHeld ? (
        <div className="mt-6 flex flex-col gap-2 sm:flex-row">
          <button
            type="button"
            onClick={() => confirmMutation.mutate()}
            disabled={confirmMutation.isPending || cancelMutation.isPending}
            className="rounded bg-emerald-700 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-800 disabled:cursor-not-allowed disabled:bg-slate-300"
          >
            {confirmMutation.isPending ? "Confirming…" : "Pay Admission Fee"}
          </button>
          <button
            type="button"
            onClick={() => cancelMutation.mutate()}
            disabled={confirmMutation.isPending || cancelMutation.isPending}
            className="rounded border border-slate-300 px-4 py-2 text-sm font-medium hover:bg-slate-100 disabled:cursor-not-allowed disabled:text-slate-400"
          >
            {cancelMutation.isPending ? "Cancelling…" : "Cancel"}
          </button>
        </div>
      ) : null}
    </main>
  );
}

function StatusBadge({ status }: { status: string }) {
  const tone =
    status === "HELD"
      ? "bg-amber-100 text-amber-900"
      : status === "CONFIRMED"
        ? "bg-emerald-100 text-emerald-900"
        : status === "EXPIRED"
          ? "bg-red-100 text-red-900"
          : "bg-slate-200 text-slate-800";
  return (
    <span className={`rounded px-2 py-0.5 text-xs font-semibold ${tone}`} data-testid="hold-status">
      {status}
    </span>
  );
}
