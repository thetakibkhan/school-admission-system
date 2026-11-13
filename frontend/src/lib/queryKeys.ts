// Query keys always carry the student id for student-scoped data, so switching
// students can never render the previous student's cached rows.

export const queryKeys = {
  classes: ["classes"] as const,
  class: (classId: string) => ["classes", classId] as const,
  hold: (holdId: string) => ["holds", holdId] as const,
  myHolds: (studentId: string) => ["me", "holds", studentId] as const,
};

/** Prefix used to remove every student-scoped entry when the id is cleared. */
export const studentScopedQueryKeys = ["me"] as const;
