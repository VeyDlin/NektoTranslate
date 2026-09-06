import type { StatusMessage } from "@/types/models/domain";
import { FetchError } from "ofetch";

import { describe } from "./status";


// What the server had to say when a request failed, as one sentence a person can act on.
//
// The server answers a refusal with `{ status }` - the same code-plus-English shape every other
// status travels in, so it is translated the same way. A request the framework rejected before it
// reached a controller carries ProblemDetails instead, with a per-field `errors` map for a body that
// did not validate. Anything else - the server not running, a connection dropped - has no body at
// all, and saying so plainly beats surfacing an ofetch stack trace.
export function describeFailure(error: unknown): string {
    if (error instanceof FetchError) {
        const body: unknown = error.data;

        if (isStatusEnvelope(body)) {
            return describe(body.status);
        }

        if (isProblemDetails(body)) {
            const fieldMessages = Object.values(body.errors ?? {}).flat();

            if (fieldMessages.length > 0) {
                return fieldMessages.join(" ");
            }

            return body.detail ?? body.title ?? `The server answered ${error.statusCode ?? "with an error"}.`;
        }

        if (error.statusCode === undefined) {
            return "The server did not answer. Is it running?";
        }

        return `The server answered ${error.statusCode} and gave no reason.`;
    }

    if (error instanceof Error && error.message.trim().length > 0) {
        return error.message;
    }

    return "Something went wrong, and nothing said what.";
}


interface StatusEnvelope {
    status: StatusMessage;
}


interface ProblemDetails {
    title?: string;
    detail?: string;
    errors?: Record<string, string[]>;
}


function isStatusEnvelope(body: unknown): body is StatusEnvelope {
    if (typeof body !== "object" || body === null || !("status" in body)) {
        return false;
    }

    const status: unknown = body.status;

    return typeof status === "object" && status !== null && "code" in status && "text" in status;
}


function isProblemDetails(body: unknown): body is ProblemDetails {
    return typeof body === "object" && body !== null && ("title" in body || "detail" in body || "errors" in body);
}
