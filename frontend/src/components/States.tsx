import type { ReactNode } from "react";
import { describeError } from "../lib/errorMessages";

export function LoadingState({ label = "Loading…" }: { label?: string }) {
  return (
    <p role="status" className="py-6 text-sm text-slate-600">
      {label}
    </p>
  );
}

export function EmptyState({ children }: { children: ReactNode }) {
  return (
    <div className="rounded border border-dashed border-slate-300 bg-white p-6 text-sm text-slate-600">
      {children}
    </div>
  );
}

export function ErrorState({
  error,
  onRetry,
}: {
  error: unknown;
  onRetry?: () => void;
}) {
  const described = describeError(error);
  return (
    <div role="alert" className="rounded border border-red-300 bg-red-50 p-4 text-sm text-red-900">
      <p className="font-semibold">{described.message}</p>
      <p className="mt-1">{described.nextStep}</p>
      {described.retryable && onRetry !== undefined ? (
        <button
          type="button"
          onClick={onRetry}
          className="mt-3 rounded bg-red-700 px-3 py-1.5 font-medium text-white hover:bg-red-800"
        >
          Retry
        </button>
      ) : null}
    </div>
  );
}

export function InlineError({ error }: { error: unknown }) {
  const described = describeError(error);
  return (
    <div role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-900">
      <p className="font-semibold">{described.message}</p>
      <p className="mt-1">{described.nextStep}</p>
    </div>
  );
}
