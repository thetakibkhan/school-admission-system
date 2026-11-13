import { createContext, createElement, useCallback, useContext, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { studentScopedQueryKeys } from "../lib/queryKeys";

export const STUDENT_ID_STORAGE_KEY = "school-admission.studentId";
export const STUDENT_ID_PATTERN = /^[0-9]{17}$/;

export function isValidStudentId(value: string): boolean {
  return STUDENT_ID_PATTERN.test(value);
}

function readStoredStudentId(): string | null {
  try {
    const stored = window.localStorage.getItem(STUDENT_ID_STORAGE_KEY);
    return stored !== null && isValidStudentId(stored) ? stored : null;
  } catch {
    return null;
  }
}

export interface StudentIdContextValue {
  studentId: string | null;
  setStudentId: (value: string) => boolean;
  clearStudentId: () => void;
}

const StudentIdContext = createContext<StudentIdContextValue | null>(null);

export function StudentIdProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [studentId, setStudentIdState] = useState<string | null>(() => readStoredStudentId());

  const setStudentId = useCallback(
    (value: string): boolean => {
      if (!isValidStudentId(value)) {
        return false;
      }
      try {
        window.localStorage.setItem(STUDENT_ID_STORAGE_KEY, value);
      } catch {
        // Storage can be unavailable (private mode); the id still works for this session.
      }
      setStudentIdState(value);
      return true;
    },
    [],
  );

  const clearStudentId = useCallback(() => {
    try {
      window.localStorage.removeItem(STUDENT_ID_STORAGE_KEY);
    } catch {
      // Nothing to remove when storage is unavailable.
    }
    // Drop every student-scoped cache entry before another student's data can render.
    queryClient.removeQueries({ queryKey: studentScopedQueryKeys });
    setStudentIdState(null);
  }, [queryClient]);

  const value = useMemo<StudentIdContextValue>(
    () => ({ studentId, setStudentId, clearStudentId }),
    [studentId, setStudentId, clearStudentId],
  );

  return createElement(StudentIdContext.Provider, { value }, children);
}

export function useStudentId(): StudentIdContextValue {
  const context = useContext(StudentIdContext);
  if (context === null) {
    throw new Error("useStudentId must be used inside StudentIdProvider");
  }
  return context;
}

/** Masks an id to its last four digits for display. */
export function maskStudentId(studentId: string): string {
  const tail = studentId.slice(-4);
  return `•••••••••••••${tail}`;
}
