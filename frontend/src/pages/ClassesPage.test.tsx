import { describe, expect, it, beforeEach, afterEach, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { App } from "../App";
import { STUDENT_ID_STORAGE_KEY } from "../hooks/useStudentId";
import { renderWithProviders } from "../test/renderWithProviders";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("ClassesPage apply", () => {
  beforeEach(() => {
    window.localStorage.setItem(STUDENT_ID_STORAGE_KEY, "20152691234567890");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.clear();
  });

  it("shows the server message on a 409 no-seats response and stays on the class list", async () => {
    const user = userEvent.setup();

    // The cached list still shows one free seat; the server is authoritative and
    // answers 409. The UI must show the server's message and not navigate.
    vi.stubGlobal(
      "fetch",
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        const method = init?.method ?? "GET";
        if (url.endsWith("/classes") && method === "GET") {
          return Promise.resolve(
            jsonResponse([
              {
                id: "c_1",
                name: "Class 6 - Morning Shift",
                totalSeats: 10,
                heldSeats: 8,
                confirmedSeats: 1,
                availableSeats: 1,
              },
            ]),
          );
        }
        if (url.includes("/classes/c_1/holds") && method === "POST") {
          return Promise.resolve(
            jsonResponse(
              {
                type: "about:blank",
                title: "Conflict",
                status: 409,
                detail: "All seats are currently occupied.",
              },
              409,
            ),
          );
        }
        return Promise.reject(new Error(`Unexpected request: ${method} ${url}`));
      }),
    );

    renderWithProviders(<App />, { route: "/" });

    const applyButton = await screen.findByRole("button", {
      name: "Apply for Class 6 - Morning Shift",
    });
    expect(applyButton).toBeEnabled();
    await user.click(applyButton);

    expect(await screen.findByText("All seats are currently occupied.")).toBeInTheDocument();
    // Still on the class list: no hold detail page was opened.
    expect(screen.getByRole("heading", { name: "Classes" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Application" })).toBeNull();
  });

  it("disables Apply when no student id is set", async () => {
    window.localStorage.clear();
    vi.stubGlobal(
      "fetch",
      vi.fn(() =>
        Promise.resolve(
          jsonResponse([
            {
              id: "c_1",
              name: "Class 6 - Morning Shift",
              totalSeats: 10,
              heldSeats: 0,
              confirmedSeats: 0,
              availableSeats: 10,
            },
          ]),
        ),
      ),
    );

    renderWithProviders(<App />, { route: "/" });

    const applyButton = await screen.findByRole("button", {
      name: "Apply for Class 6 - Morning Shift",
    });
    expect(applyButton).toBeDisabled();
    expect(screen.getByText(/Set your 17-digit student ID above/i)).toBeInTheDocument();
  });
});
