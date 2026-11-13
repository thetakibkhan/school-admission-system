import { ApiError, NetworkError } from "../api/client";

export interface UserFacingError {
  message: string;
  nextStep: string;
  /** True when a Retry button makes sense (network or server-side failure). */
  retryable: boolean;
}

/**
 * Maps a failure to a distinct, non-technical message with a next step.
 * 400, 404, 409 and network/server failures each read differently (US-08.4).
 */
export function describeError(error: unknown): UserFacingError {
  if (error instanceof NetworkError) {
    return {
      message: "We could not reach the server.",
      nextStep: "Check your connection, then retry.",
      retryable: true,
    };
  }

  if (error instanceof ApiError) {
    if (error.status === 400) {
      return {
        message: error.detail || "Some details were not accepted.",
        nextStep: "Correct the highlighted fields and submit again.",
        retryable: false,
      };
    }
    if (error.status === 404) {
      return {
        message: error.detail || "We could not find that record.",
        nextStep: "Go back to the class list and start again.",
        retryable: false,
      };
    }
    if (error.status === 409) {
      return {
        message: error.detail || "That action is not possible right now.",
        nextStep: "The list has been refreshed. Pick another class or try again later.",
        retryable: false,
      };
    }
    if (error.status >= 500) {
      return {
        message: "The server had a temporary problem.",
        nextStep: "Wait a moment, then retry.",
        retryable: true,
      };
    }
    return {
      message: error.detail || error.title || "The request failed.",
      nextStep: "Retry, or go back to the class list.",
      retryable: true,
    };
  }

  return {
    message: "Something unexpected happened.",
    nextStep: "Retry, or go back to the class list.",
    retryable: true,
  };
}
