import { describe, expect, it, beforeEach, afterEach, vi } from "vitest";
import { screen } from "@testing-library/react";
import { Route, Routes } from "react-router-dom";
import { HoldDetailPage } from "./HoldDetailPage";
import { STUDENT_ID_STORAGE_KEY } from "../hooks/useStudentId";
import { renderWithProviders } from "../test/renderWithProviders";

function jsonResponse(body: unknown, status = 200, serverDate?: string): Response {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (serverDate !== undefined) {
    headers["Date"] = serverDate;
  }
  return new Response(JSON.stringify(body), { status, headers });
}

function renderHoldDetail() {
  return renderWithProviders(
    <Routes>
      <Route path="/holds/:holdId" element={<HoldDetailPage />} />
    </Routes>,
    { route: "/holds/h_1" },
  );
}

describe("HoldDetailPage", () => {
  beforeEach(() => {
    window.localStorage.setItem(STUDENT_ID_STORAGE_KEY, "20152691234567890");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.clear();
  });

  it("renders EXPIRED and disables payment for a HELD hold whose expiresAt is in the past", async () => {
    const pastExpiry = new Date(Date.now() - 60_000).toISOString().replace(".000Z", "Z");

    vi.stubGlobal(
      "fetch",
      vi.fn((input: RequestInfo | URL) => {
        const url = String(input);
        if (url.includes("/holds/h_1")) {
          return Promise.resolve(
            jsonResponse({
              holdId: "h_1",
              classId: "c_1",
              status: "HELD",
              expiresAt: pastExpiry,
            }),
          );
        }
        if (url.includes("/classes/c_1")) {
          return Promise.resolve(
            jsonResponse({
              id: "c_1",
              name: "Class 6 - Morning Shift",
              totalSeats: 120,
              heldSeats: 1,
              confirmedSeats: 0,
              availableSeats: 119,
            }),
          );
        }
        return Promise.reject(new Error(`Unexpected request: ${url}`));
      }),
    );

    renderHoldDetail();

    expect(await screen.findByTestId("hold-status")).toHaveTextContent("EXPIRED");
    expect(screen.queryByRole("button", { name: "Pay Admission Fee" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Cancel" })).toBeNull();
    expect(screen.getByText(/This reservation has expired/i)).toBeInTheDocument();
  });

  it("shows HELD with a countdown and both actions while the hold is still live", async () => {
    const futureExpiry = new Date(Date.now() + 5 * 60_000).toISOString().replace(".000Z", "Z");

    vi.stubGlobal(
      "fetch",
      vi.fn((input: RequestInfo | URL) => {
        const url = String(input);
        if (url.includes("/holds/h_1")) {
          return Promise.resolve(
            jsonResponse({
              holdId: "h_1",
              classId: "c_1",
              status: "HELD",
              expiresAt: futureExpiry,
            }),
          );
        }
        if (url.includes("/classes/c_1")) {
          return Promise.resolve(
            jsonResponse({
              id: "c_1",
              name: "Class 6 - Morning Shift",
              totalSeats: 120,
              heldSeats: 1,
              confirmedSeats: 0,
              availableSeats: 119,
            }),
          );
        }
        return Promise.reject(new Error(`Unexpected request: ${url}`));
      }),
    );

    renderHoldDetail();

    expect(await screen.findByTestId("hold-status")).toHaveTextContent("HELD");
    expect(screen.getByTestId("countdown")).toHaveTextContent(/^0[45]:\d{2}$/);
    expect(screen.getByRole("button", { name: "Pay Admission Fee" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeEnabled();
  });

  it("keeps a server-HELD hold actionable when the browser clock is ahead of the server", async () => {
    // The browser clock is 10 minutes ahead of the server. The hold's expiresAt is
    // already in the past by the browser clock but still 5 minutes away on the server
    // clock (the Date header). The server's answer must win: HELD, actions available.
    const localNow = Date.now();
    const serverNowMs = localNow - 10 * 60_000;
    const expiresAt = new Date(localNow - 5 * 60_000).toISOString().replace(".000Z", "Z");
    const serverDate = new Date(serverNowMs).toUTCString();

    vi.stubGlobal(
      "fetch",
      vi.fn((input: RequestInfo | URL) => {
        const url = String(input);
        if (url.includes("/holds/h_1")) {
          return Promise.resolve(
            jsonResponse(
              { holdId: "h_1", classId: "c_1", status: "HELD", expiresAt },
              200,
              serverDate,
            ),
          );
        }
        if (url.includes("/classes/c_1")) {
          return Promise.resolve(
            jsonResponse({
              id: "c_1",
              name: "Class 6 - Morning Shift",
              totalSeats: 120,
              heldSeats: 1,
              confirmedSeats: 0,
              availableSeats: 119,
            }),
          );
        }
        return Promise.reject(new Error(`Unexpected request: ${url}`));
      }),
    );

    renderHoldDetail();

    expect(await screen.findByTestId("hold-status")).toHaveTextContent("HELD");
    expect(screen.getByRole("button", { name: "Pay Admission Fee" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeEnabled();
    // Countdown is measured on the server clock: about 5 minutes, not zero.
    expect(screen.getByTestId("countdown")).toHaveTextContent(/^0[45]:\d{2}$/);
    expect(screen.queryByText(/This reservation has expired/i)).toBeNull();
  });
});
