import { useState } from "react";
import type { FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createClass } from "../api/classes";
import { queryKeys } from "../lib/queryKeys";
import { InlineError } from "../components/States";

interface FieldErrors {
  name?: string;
  totalSeats?: string;
}

export function CreateClassPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [totalSeats, setTotalSeats] = useState("");
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});

  const mutation = useMutation({
    mutationFn: createClass,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.classes });
      navigate("/");
    },
  });

  function validate(): FieldErrors {
    const errors: FieldErrors = {};
    if (name.trim().length === 0) {
      errors.name = "Enter a class name.";
    }
    const seats = Number(totalSeats);
    if (totalSeats.trim().length === 0) {
      errors.totalSeats = "Enter the number of seats.";
    } else if (!Number.isInteger(seats) || seats <= 0) {
      errors.totalSeats = "Seats must be a whole number greater than zero.";
    }
    return errors;
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const errors = validate();
    setFieldErrors(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }
    mutation.mutate({ name: name.trim(), totalSeats: Number(totalSeats) });
  }

  return (
    <main className="mx-auto max-w-xl px-4 py-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-slate-900">Create Class</h1>
        <Link to="/" className="text-sm text-slate-700 underline">
          Back to classes
        </Link>
      </div>

      <form onSubmit={handleSubmit} noValidate className="mt-6 space-y-4">
        <div>
          <label htmlFor="class-name" className="block text-sm font-medium text-slate-800">
            Class name
          </label>
          <input
            id="class-name"
            name="name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            aria-invalid={fieldErrors.name !== undefined}
            aria-describedby={fieldErrors.name !== undefined ? "class-name-error" : undefined}
            className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
            placeholder="Class 6 - Morning Shift"
          />
          {fieldErrors.name !== undefined ? (
            <p id="class-name-error" role="alert" className="mt-1 text-sm text-red-700">
              {fieldErrors.name}
            </p>
          ) : null}
        </div>

        <div>
          <label htmlFor="class-seats" className="block text-sm font-medium text-slate-800">
            Total seats
          </label>
          <input
            id="class-seats"
            name="totalSeats"
            inputMode="numeric"
            value={totalSeats}
            onChange={(event) => setTotalSeats(event.target.value)}
            aria-invalid={fieldErrors.totalSeats !== undefined}
            aria-describedby={fieldErrors.totalSeats !== undefined ? "class-seats-error" : undefined}
            className="mt-1 w-full rounded border border-slate-300 px-3 py-2 text-sm"
            placeholder="120"
          />
          {fieldErrors.totalSeats !== undefined ? (
            <p id="class-seats-error" role="alert" className="mt-1 text-sm text-red-700">
              {fieldErrors.totalSeats}
            </p>
          ) : null}
        </div>

        {mutation.isError ? <InlineError error={mutation.error} /> : null}

        <button
          type="submit"
          disabled={mutation.isPending}
          className="rounded bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700 disabled:cursor-not-allowed disabled:bg-slate-300"
        >
          {mutation.isPending ? "Creating…" : "Create class"}
        </button>
      </form>
    </main>
  );
}
