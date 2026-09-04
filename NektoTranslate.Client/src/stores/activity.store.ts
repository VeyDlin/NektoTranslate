import type { Activity, ImportItemState, ImportJob, ImportKind, JobState, StatusMessage } from "@/types/models/domain";
import { defineStore } from "pinia";

import { computed, ref } from "vue";


// A patch carries already-decoded values, the same convention run.store's `applyJobEvent` uses —
// decoding happens once, at the SignalR boundary in useTranslationStream, not here.
interface ImportStatePatch {
    jobId: number;
    kind: ImportKind;
    state: JobState;
    processed: number;
    total: number;
    currentTitle: string | null;
}


interface ImportItemPatch {
    jobId: number;
    position: number;
    sourceUrl: string;
    title: string;
    chapterIndex: number | null;
    chapterId: number | null;
    state: ImportItemState;
    status: StatusMessage | null;
    finishedAt: string;
}


// The import jobs of the current novel, keyed by id, each carrying its own items. Kept apart from
// run.store: a translation run is one thing a novel does at a time, but an import of the originals
// and an import of a translation can be running side by side, and each of the two screens needs to
// find its own without the other's progress bleeding into it.
export const useActivityStore = defineStore("activity", () => {
    const jobs = ref<Record<number, ImportJob>>({});

    const activeImports = computed(() => Object.values(jobs.value).filter(job => (
        job.state === "Queued" || job.state === "Running" || job.state === "Paused"
    )));

    const isImporting = computed(() => activeImports.value.length > 0);


    // Whichever job — active, or settled but not yet dismissed — currently represents this kind. At
    // most one ever sits in the store per kind: `adoptJob` replaces it the moment a new one starts,
    // and nothing else adds a second.
    function importFor(kind: ImportKind): ImportJob | null {
        const matches = Object.values(jobs.value).filter(job => job.kind === kind);

        if (matches.length === 0) {
            return null;
        }

        return matches.reduce((latest, job) => (job.id > latest.id ? job : latest));
    }


    // From `GET /activity`, which only ever reports live jobs. Upserted rather than replacing the map
    // outright, so a job that settled while this session was watching stays visible — with its
    // Dismiss control — through a refetch that would otherwise have stopped mentioning it.
    function adoptActivity(activity: Activity): void {
        if (activity.imports.length === 0) {
            return;
        }

        const patch: Record<number, ImportJob> = {};

        for (const job of activity.imports) {
            patch[job.id] = job;
        }

        jobs.value = { ...jobs.value, ...patch };
    }


    // From the response to starting a job, which is the one place a full `ImportJobItem[]` is known
    // up front.
    function adoptJob(job: ImportJob): void {
        jobs.value = { ...jobs.value, [job.id]: job };
    }


    function applyImportState(patch: ImportStatePatch): void {
        const existing = jobs.value[patch.jobId];

        // The job has not been adopted yet — GET /activity or the start response has not landed. The
        // next event carries the same fields, so nothing is lost by waiting for one of those instead
        // of fabricating an entry from a partial event.
        if (existing === undefined) {
            return;
        }

        jobs.value = {
            ...jobs.value,
            [patch.jobId]: {
                ...existing,
                kind: patch.kind,
                state: patch.state,
                processedCount: patch.processed,
                totalCount: patch.total,
                currentTitle: patch.currentTitle,
            },
        };
    }


    function applyImportItem(patch: ImportItemPatch): void {
        const job = jobs.value[patch.jobId];

        if (job === undefined || job.items === null) {
            return;
        }

        const items = job.items.map(item => (item.position === patch.position
            ? {
                    ...item,
                    sourceUrl: patch.sourceUrl,
                    title: patch.title,
                    chapterIndex: patch.chapterIndex,
                    chapterId: patch.chapterId,
                    state: patch.state,
                    status: patch.status,
                    finishedAt: patch.finishedAt,
                }
            : item));

        jobs.value = { ...jobs.value, [patch.jobId]: { ...job, items } };
    }


    // The run stays on screen after it settles so the user can read what happened; this is what the
    // Dismiss control calls once they are done with it.
    function clearSettled(jobId: number): void {
        const next = { ...jobs.value };

        delete next[jobId];
        jobs.value = next;
    }


    return {
        jobs,
        activeImports,
        isImporting,
        importFor,
        adoptActivity,
        adoptJob,
        applyImportState,
        applyImportItem,
        clearSettled,
    };
});
