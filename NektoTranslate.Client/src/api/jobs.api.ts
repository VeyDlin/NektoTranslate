import type { StartTranslationJobRequest } from "@/types/api/requests";

import type { TranslationJob } from "@/types/models/domain";
import { decodeJobScopeKind, decodeJobState } from "@/utils/wire";
import { apiClient } from "./client";


// Exported so imports.api.ts can decode the `translation` half of `GET .../activity` with the exact
// same rules, rather than a second copy of this decoding drifting out of sync with it.
export interface RawTranslationJob extends Omit<TranslationJob, "scopeKind" | "state"> {
    scopeKind: number | string;
    state: number | string;
}


export function toJob(raw: RawTranslationJob): TranslationJob {
    return {
        ...raw,
        scopeKind: decodeJobScopeKind(raw.scopeKind),
        state: decodeJobState(raw.state),
    };
}


export const jobsApi = {
    list(novelId: number): Promise<TranslationJob[]> {
        return apiClient<RawTranslationJob[]>(`/api/novels/${novelId}/jobs`).then(rows => rows.map(toJob));
    },

    start(novelId: number, request: StartTranslationJobRequest): Promise<TranslationJob> {
        // Sent as the enum's name. The server reads names since it added JsonStringEnumConverter,
        // and the domain type already carries the name, so nothing has to be encoded.
        return apiClient<RawTranslationJob>(`/api/novels/${novelId}/jobs`, {
            method: "POST",
            body: request,
        }).then(toJob);
    },

    cancel(novelId: number, jobId: number): Promise<void> {
        return apiClient<void>(`/api/novels/${novelId}/jobs/${jobId}/cancel`, {
            method: "POST",
        });
    },
};
