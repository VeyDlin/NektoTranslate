import type { UpsertParserScriptRequest } from "@/types/api/requests";
import type { ParserScriptSummary } from "@/types/models/domain";

import { apiClient } from "./client";


export const parsersApi = {
    list(): Promise<ParserScriptSummary[]> {
        return apiClient<ParserScriptSummary[]>("/api/parsers");
    },

    source(hostName: string): Promise<string> {
        return apiClient<string>(`/api/parsers/${encodeURIComponent(hostName)}/source`);
    },

    upsert(hostName: string, request: UpsertParserScriptRequest): Promise<unknown> {
        return apiClient(`/api/parsers/${encodeURIComponent(hostName)}`, {
            method: "PUT",
            body: request,
        });
    },

    // Deleting an override restores the bundled parser, because the bundled one was never replaced —
    // only shadowed.
    revert(hostName: string): Promise<void> {
        return apiClient<void>(`/api/parsers/${encodeURIComponent(hostName)}`, {
            method: "DELETE",
        });
    },
};
