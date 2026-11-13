import { request } from "./client";
import type { ClassCreatedResponse, ClassResponse, CreateClassRequest } from "./types";

export function listClasses(signal?: AbortSignal): Promise<ClassResponse[]> {
  return request<ClassResponse[]>("/classes", { signal });
}

export function getClass(classId: string, signal?: AbortSignal): Promise<ClassResponse> {
  return request<ClassResponse>(`/classes/${encodeURIComponent(classId)}`, { signal });
}

export function createClass(payload: CreateClassRequest): Promise<ClassCreatedResponse> {
  return request<ClassCreatedResponse>("/classes", { method: "POST", body: payload });
}
