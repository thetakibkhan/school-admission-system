import { request } from "./client";
import type {
  HoldConfirmedResponse,
  HoldCreatedResponse,
  HoldResponse,
  MyHoldResponse,
} from "./types";

export function createHold(classId: string, studentId: string): Promise<HoldCreatedResponse> {
  return request<HoldCreatedResponse>(`/classes/${encodeURIComponent(classId)}/holds`, {
    method: "POST",
    studentId,
  });
}

export function getHold(holdId: string, signal?: AbortSignal): Promise<HoldResponse> {
  return request<HoldResponse>(`/holds/${encodeURIComponent(holdId)}`, { signal });
}

export function confirmHold(
  holdId: string,
  studentId: string,
): Promise<HoldConfirmedResponse> {
  return request<HoldConfirmedResponse>(`/holds/${encodeURIComponent(holdId)}/confirm`, {
    method: "POST",
    studentId,
  });
}

export function cancelHold(holdId: string, studentId: string): Promise<void> {
  return request<void>(`/holds/${encodeURIComponent(holdId)}`, {
    method: "DELETE",
    studentId,
  });
}

export function listMyHolds(studentId: string, signal?: AbortSignal): Promise<MyHoldResponse[]> {
  return request<MyHoldResponse[]>("/me/holds", { studentId, signal });
}
