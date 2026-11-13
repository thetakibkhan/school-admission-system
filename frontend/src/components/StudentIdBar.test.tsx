import { describe, expect, it, beforeEach } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StudentIdBar } from "../components/StudentIdBar";
import { STUDENT_ID_STORAGE_KEY } from "../hooks/useStudentId";
import { renderWithProviders } from "../test/renderWithProviders";

describe("StudentIdBar", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("rejects a 16-digit id with a validation error and does not save it", async () => {
    const user = userEvent.setup();
    renderWithProviders(<StudentIdBar />);

    await user.click(screen.getByRole("button", { name: "Set ID" }));
    await user.type(screen.getByLabelText("Student ID (17 digits)"), "1234567890123456");
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Enter exactly 17 digits.");
    expect(window.localStorage.getItem(STUDENT_ID_STORAGE_KEY)).toBeNull();
    expect(screen.queryByTestId("active-student-id")).toBeNull();
  });

  it("saves a valid 17-digit id and masks it to the last four digits", async () => {
    const user = userEvent.setup();
    renderWithProviders(<StudentIdBar />);

    await user.click(screen.getByRole("button", { name: "Set ID" }));
    await user.type(screen.getByLabelText("Student ID (17 digits)"), "20152691234567890");
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByTestId("active-student-id")).toHaveTextContent("•••••••••••••7890");
    expect(window.localStorage.getItem(STUDENT_ID_STORAGE_KEY)).toBe("20152691234567890");
  });
});
