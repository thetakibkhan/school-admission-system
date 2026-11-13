import type { HoldStatus } from "../api/types";

/**
 * The server is authoritative, but the UI must show EXPIRED the moment the
 * countdown reaches zero without a reload. A stored HELD whose expiresAt is at
 * or before now is effectively EXPIRED (PRD 5.3: eligible only while now < expiresAt).
 */
export function effectiveStatus(
  status: HoldStatus,
  expiresAt: string,
  nowMs: number = Date.now(),
): HoldStatus {
  if (status !== "HELD") {
    return status;
  }
  const expiry = Date.parse(expiresAt);
  if (Number.isNaN(expiry)) {
    return status;
  }
  return nowMs >= expiry ? "EXPIRED" : "HELD";
}

/** Remaining milliseconds until expiry, never negative. */
export function remainingMs(expiresAt: string, nowMs: number = Date.now()): number {
  const expiry = Date.parse(expiresAt);
  if (Number.isNaN(expiry)) {
    return 0;
  }
  return Math.max(0, expiry - nowMs);
}

/** Formats a duration as mm:ss (or h:mm:ss past an hour). */
export function formatCountdown(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const pad = (value: number) => value.toString().padStart(2, "0");
  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${pad(minutes)}:${pad(seconds)}`;
}

/** Human-readable UTC timestamp for display. */
export function formatTimestamp(iso: string): string {
  const parsed = Date.parse(iso);
  if (Number.isNaN(parsed)) {
    return iso;
  }
  return new Date(parsed).toISOString().replace("T", " ").replace(".000Z", " UTC");
}
