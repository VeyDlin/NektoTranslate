import type { RawTranslationJob } from "./jobs.api";
import type { StartImportRequest } from "@/types/api/requests";
import type { Activity, ImportJob, ImportJobItem } from "@/types/models/domain";

import { decodeImportItemState, decodeImportKind, decodeJobState } from "@/utils/wire";
import { apiClient } from "./client";
import { toJob } from "./jobs.api";


interface RawImportJobItem extends Omit<ImportJobItem, "state"> {
    state: number | string;
}


interface RawImportJob extends Omit<ImportJob, "kind" | "state" | "items"> {
    kind: number | string;
    state: number | string;
    items: RawImportJobItem[] | null;
}


interface RawActivity {
    translation: RawTranslationJob | null;
    imports: RawImportJob[];
}


function toItem(raw: RawImportJobItem): ImportJobItem {
    return { ...raw, state: decodeImportItemState(raw.state) };
}


function toImportJob(raw: RawImportJob): ImportJob {
    return {
        ...raw,
        kind: decodeImportKind(raw.kind),
        state: decodeJobState(raw.state),
        items: raw.items === null ? null : raw.items.map(toItem),
    };
}


function toActivity(raw: RawActivity): Activity {
    return {
        translation: raw.translation === null ? null : toJob(raw.translation),
        imports: raw.imports.map(toImportJob),
    };
}


export const importsApi = {
    // Chapters the user already picked, not a contents URL — the same rule the old one-shot endpoints
    // followed, kept here because it is still what stops a two-thousand-chapter novel being pulled
    // down whole because a link was pasted.
    start(novelId: number, request: StartImportRequest): Promise<ImportJob> {
        return apiClient<RawImportJob>(`/api/novels/${novelId}/imports`, {
            method: "POST",
            body: request,
        }).then(toImportJob);
    },

    list(novelId: number): Promise<ImportJob[]> {
        return apiClient<RawImportJob[]>(`/api/novels/${novelId}/imports`).then(rows => rows.map(toImportJob));
    },

    getById(novelId: number, jobId: number): Promise<ImportJob> {
        return apiClient<RawImportJob>(`/api/novels/${novelId}/imports/${jobId}`).then(toImportJob);
    },

    pause(novelId: number, jobId: number): Promise<void> {
        return apiClient<void>(`/api/novels/${novelId}/imports/${jobId}/pause`, { method: "POST" });
    },

    resume(novelId: number, jobId: number): Promise<void> {
        return apiClient<void>(`/api/novels/${novelId}/imports/${jobId}/resume`, { method: "POST" });
    },

    cancel(novelId: number, jobId: number): Promise<void> {
        return apiClient<void>(`/api/novels/${novelId}/imports/${jobId}/cancel`, { method: "POST" });
    },

    // Re-fetches and re-imports one item of a settled job, in place — the retry a failed row in the
    // report offers. The server refuses with a 409 while the job is still active; there is nothing
    // this method needs to do about that beyond letting it reach the caller.
    retryItem(novelId: number, jobId: number, position: number): Promise<ImportJobItem> {
        return apiClient<RawImportJobItem>(
            `/api/novels/${novelId}/imports/${jobId}/items/${position}/retry`,
            { method: "POST" },
        ).then(toItem);
    },

    // Only the jobs still Queued, Running or Paused, alongside whatever translation run is live — one
    // request answers "what is happening to this book right now", which is what a reloaded page needs
    // to land back where it was rather than replaying the event history from nothing.
    activity(novelId: number): Promise<Activity> {
        return apiClient<RawActivity>(`/api/novels/${novelId}/activity`).then(toActivity);
    },
};
