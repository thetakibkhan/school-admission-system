// One request helper for every endpoint. It never throws a raw Response: a non-2xx
// answer becomes a typed ApiError carrying the Problem Details fields (R9).

export interface ApiErrorInit {
  status: number;
  title: string;
  detail: string;
}

export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly detail: string;

  constructor({ status, title, detail }: ApiErrorInit) {
    super(detail || title);
    this.name = "ApiError";
    this.status = status;
    this.title = title;
    this.detail = detail;
  }
}

/** A network-level failure: the request never reached the server. */
export class NetworkError extends Error {
  constructor(message = "The server could not be reached.") {
    super(message);
    this.name = "NetworkError";
  }
}

const API_BASE_URL: string = import.meta.env.VITE_API_BASE_URL ?? "";

export interface RequestOptions {
  method?: "GET" | "POST" | "DELETE";
  body?: unknown;
  /** Sent as X-Student-Id for student-scoped calls. Never placed in the URL. */
  studentId?: string;
  signal?: AbortSignal;
}

interface ProblemDetailsBody {
  type?: unknown;
  title?: unknown;
  status?: unknown;
  detail?: unknown;
}

function asString(value: unknown): string | undefined {
  return typeof value === "string" && value.length > 0 ? value : undefined;
}

async function parseProblem(response: Response): Promise<ApiError> {
  let body: ProblemDetailsBody = {};
  try {
    body = (await response.json()) as ProblemDetailsBody;
  } catch {
    // A non-JSON error body still becomes a typed ApiError below.
  }

  const status = typeof body.status === "number" ? body.status : response.status;
  const title = asString(body.title) ?? response.statusText ?? "Request failed";
  const detail = asString(body.detail) ?? "";

  return new ApiError({ status, title, detail });
}

export interface ResponseMeta {
  /** Server clock at response time, from the Date header (falls back to local receipt). */
  serverTimeMs: number;
  /** Local clock when the response was received. */
  receivedAtMs: number;
  /** True when the server actually supplied its clock (a Date header). */
  hasServerClock: boolean;
}

export interface RequestResult<T> {
  data: T;
  meta: ResponseMeta;
}

function readMeta(response: Response, receivedAtMs: number): ResponseMeta {
  const header = response.headers.get("Date");
  const parsed = header === null ? Number.NaN : Date.parse(header);
  const hasServerClock = !Number.isNaN(parsed);
  return {
    serverTimeMs: hasServerClock ? parsed : receivedAtMs,
    receivedAtMs,
    hasServerClock,
  };
}

export async function requestWithMeta<T>(
  path: string,
  options: RequestOptions = {},
): Promise<RequestResult<T>> {
  const { method = "GET", body, studentId, signal } = options;

  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (studentId !== undefined) {
    headers["X-Student-Id"] = studentId;
  }

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw error;
    }
    throw new NetworkError();
  }

  const receivedAtMs = Date.now();
  const meta = readMeta(response, receivedAtMs);

  if (!response.ok) {
    throw await parseProblem(response);
  }

  if (response.status === 204) {
    return { data: undefined as T, meta };
  }

  return { data: (await response.json()) as T, meta };
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const result = await requestWithMeta<T>(path, options);
  return result.data;
}
