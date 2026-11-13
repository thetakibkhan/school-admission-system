import { describe, expect, it, beforeEach, afterEach, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
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

  it("expires a server-HELD answer when the server-clock countdown reaches zero", async () => {
    // The first answer says HELD with an expiry 1.5 s away on the server clock. The pin
    // must not be permanent: once the server-clock countdown hits zero the UI must show
    // EXPIRED and disable payment without a reload, and the expiry refetch must fire
    // again (the old code pinned HELD forever and never refetched).
    const serverClockMs = Date.now();
    const serverDate = new Date(serverClockMs).toUTCString();
    const expiresAt = new Date(serverClockMs + 1500).toISOString().replace(".000Z", "Z");
    let holdCalls = 0;

    vi.stubGlobal(
      "fetch",
      vi.fn((input: RequestInfo | URL) => {
        const url = String(input);
        if (url.includes("/holds/h_1")) {
          holdCalls += 1;
          const status = holdCalls === 1 ? "HELD" : "EXPIRED";
          return Promise.resolve(
            jsonResponse({ holdId: "h_1", classId: "c_1", status, expiresAt }, 200, serverDate),
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

    await waitFor(
      () => {
        expect(screen.getByTestId("hold-status")).toHaveTextContent("EXPIRED");
      },
      { timeout: 5000 },
    );

    expect(screen.queryByRole("button", { name: "Pay Admission Fee" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Cancel" })).toBeNull();
    expect(screen.getByText(/This reservation has expired/i)).toBeInTheDocument();
    // The expiry refetch fired: the pin did not suppress it.
    await waitFor(() => expect(holdCalls).toBeGreaterThanOrEqual(2));
  });

  it("does not pin a stale HELD answer forever when the server keeps answering HELD", async () => {
    // The server answers HELD on every request even past the expiry (a stale read) and
    // advances its Date header normally. The page must still expire on the server-clock
    // countdown and stay expired instead of pinning HELD forever.
    const startedAtMs = Date.now();
    const expiresAt = new Date(startedAtMs + 1500).toISOString().replace(".000Z", "Z");

    vi.stubGlobal(
      "fetch",
      vi.fn((input: RequestInfo | URL) => {
        const url = String(input);
        if (url.includes("/holds/h_1")) {
          // Server clock == browser clock, and each answer carries a fresh Date.
          return Promise.resolve(
            jsonResponse(
              { holdId: "h_1", classId: "c_1", status: "HELD", expiresAt },
              200,
              new Date().toUTCString(),
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

    await waitFor(
      () => {
        expect(screen.getByTestId("hold-status")).toHaveTextContent("EXPIRED");
      },
      { timeout: 5000 },
    );

    // Give the expiry refetch time to land, then confirm the page did not flip back.
    await new Promise((resolve) => setTimeout(resolve, 600));
    expect(screen.getByTestId("hold-status")).toHaveTextContent("EXPIRED");
    expect(screen.queryByRole("button", { name: "Pay Admission Fee" })).toBeNull();
  });
});
