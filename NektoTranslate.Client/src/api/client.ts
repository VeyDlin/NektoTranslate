import type { FetchOptions } from "ofetch";
import { ofetch } from "ofetch";

import { mockBackend } from "./mocks/backend";


// No Authorization header and no 401 handling on purpose. The server is a loopback-bound
// single-user process with no accounts by design, so an auth interceptor here would be dead code
// that quietly implies a login screen exists somewhere.
const API_BASE = import.meta.env.VITE_API_BASE ?? "";

// Off by default now that there is a server to talk to. Set VITE_USE_MOCKS=true to work on screens
// without one running — the in-memory dataset and its simulated run are still there.
export const USE_MOCKS = import.meta.env.VITE_USE_MOCKS === "true";


// Mocking happens here, at the transport, rather than inside each API class. The classes below keep
// their real URLs and real signatures, so switching to the live server is one environment variable
// and touches nothing else.
export function apiClient<T>(url: string, options?: FetchOptions<"json">): Promise<T> {
    if (USE_MOCKS) {
        return mockBackend.handle(options?.method ?? "GET", url, options?.body) as Promise<T>;
    }

    return ofetch<T>(url, {
        baseURL: API_BASE,
        headers: {
            "Content-Type": "application/json",
        },
        ...options,
    });
}
