// Exact response shapes from the PRD section 6 contract and shared rule R1.
// Field names are part of the contract: do not rename or add keys here.

export type HoldStatus = "HELD" | "CONFIRMED" | "EXPIRED" | "CANCELLED";

/** GET /classes, GET /classes/{classId} */
export interface ClassResponse {
  id: string;
  name: string;
  totalSeats: number;
  heldSeats: number;
  confirmedSeats: number;
  availableSeats: number;
}

/** POST /classes */
export interface ClassCreatedResponse {
  id: string;
  name: string;
  totalSeats: number;
}

/** POST /classes/{classId}/holds */
export interface HoldCreatedResponse {
  holdId: string;
  classId: string;
  studentId: string;
  status: HoldStatus;
  expiresAt: string;
}

/** GET /holds/{holdId} */
export interface HoldResponse {
  holdId: string;
  classId: string;
  status: HoldStatus;
  expiresAt: string;
}

/** POST /holds/{holdId}/confirm */
export interface HoldConfirmedResponse {
  holdId: string;
  status: HoldStatus;
}

/** GET /me/holds */
export interface MyHoldResponse {
  holdId: string;
  classId: string;
  className: string;
  status: HoldStatus;
  expiresAt: string;
}

export interface CreateClassRequest {
  name: string;
  totalSeats: number;
}
