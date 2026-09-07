import type { MaybeRefOrGetter } from "vue";
import type { TranslationStream } from "@/realtime/events";
import type { ChapterSummary } from "@/types/models/domain";

import { useQueryClient } from "@tanstack/vue-query";
import { onScopeDispose, toValue, watch } from "vue";
import { importsApi } from "@/api";
import { createTranslationStream } from "@/realtime/stream";

import { useActivityStore } from "@/stores/activity.store";
import { useRunStore } from "@/stores/run.store";
import { decodeImportItemState, decodeImportKind, decodeJobState, decodeTranslationState } from "@/utils/wire";
import { chapterKey, chaptersKey } from "./useChapters";
import { glossaryKey } from "./useGlossary";
import { jobsKey } from "./useJobs";
import { listingKey } from "./useListing";


// Binds one novel's event stream to the query cache and the run store.
//
// Chapter state is patched into the cached list rather than invalidating it. A run over nine
// hundred chapters emits two state changes per chapter; invalidating would refetch a
// two-thousand-row table eighteen hundred times, which is precisely the traffic the body-less list
// projection exists to avoid.
export function useTranslationStream(novelId: MaybeRefOrGetter<number | null>): void {
    const queryClient = useQueryClient();
    const run = useRunStore();
    const activity = useActivityStore();

    let stream: TranslationStream | null = null;

    // Import jobs being fetched because an event named one the store had not met yet.
    const adopting = new Set<number>();

    function patchChapter(id: number, novel: number, patch: Partial<ChapterSummary>): void {
        queryClient.setQueryData<ChapterSummary[]>(chaptersKey(novel), (rows) => {
            if (rows === undefined) {
                return rows;
            }

            return rows.map(row => (row.id === id ? { ...row, ...patch } : row));
        });
    }

    function attach(novel: number): void {
        stream = createTranslationStream();

        stream.on("ChapterStateChanged", (payload) => {
            const state = decodeTranslationState(payload.state);

            patchChapter(payload.chapterId, novel, { translationState: state });
            run.setCurrentChapter(state === "Running" ? payload.chapterId : null);
        });

        stream.on("TranslationDelta", (payload) => {
            run.appendDelta(payload.chapterId, payload.text);
        });

        stream.on("ChapterTranslated", (payload) => {
            patchChapter(payload.chapterId, novel, { translationState: "Translated", glossaryState: "Analyzed" });
            run.clearDelta(payload.chapterId);
            void queryClient.invalidateQueries({ queryKey: chapterKey(novel, payload.chapterId) });
        });

        stream.on("JobStateChanged", (payload) => {
            run.applyJobEvent({
                jobId: payload.jobId,
                state: decodeJobState(payload.state),
                processed: payload.processed,
                total: payload.total,
                costUsd: payload.costUsd,
                currentStep: payload.currentStep,
                stepIndex: payload.stepIndex,
                stepCount: payload.stepCount,
            });

            void queryClient.invalidateQueries({ queryKey: jobsKey(novel) });
        });

        stream.on("GlossaryChanged", () => {
            void queryClient.invalidateQueries({ queryKey: glossaryKey(novel) });
        });

        stream.on("AgentMessage", (payload) => {
            run.pushMessage(payload.message);
        });

        stream.on("ImportStateChanged", (payload) => {
            const state = decodeJobState(payload.state);

            // A page that was open before the job started has never seen it: no start response, and
            // /activity was answered before there was anything to report. The event alone is not
            // enough to build a row from, but it is enough to know what to fetch - once, however many
            // events arrive while the fetch is in flight.
            if (activity.jobs[payload.jobId] === undefined) {
                if (!adopting.has(payload.jobId)) {
                    adopting.add(payload.jobId);

                    importsApi.getById(novel, payload.jobId)
                        .then(job => activity.adoptJob(job))
                        .catch(() => undefined)
                        .finally(() => adopting.delete(payload.jobId));
                }

                return;
            }

            activity.applyImportState({
                jobId: payload.jobId,
                kind: decodeImportKind(payload.kind),
                state,
                processed: payload.processed,
                total: payload.total,
                currentTitle: payload.currentTitle,
            });

            // Imported chapters land as the job runs, not in one batch at the end, so the chapter
            // list is only worth refetching once there is nothing left to add — settling is that
            // signal, and it fires once per job rather than once per chapter. The listing's "In the
            // book" column is a join against those same chapters, so it goes stale at the same
            // moment and is refreshed by the same signal.
            if (state === "Completed" || state === "Failed" || state === "Cancelled") {
                void queryClient.invalidateQueries({ queryKey: chaptersKey(novel) });
                void queryClient.invalidateQueries({
                    queryKey: listingKey(novel, decodeImportKind(payload.kind)),
                });
            }
        });

        // The read landed, failed or was abandoned. Only the key is refetched: the entries live in
        // the listing itself, and a screen that is not open for this kind simply keeps a stale cache
        // entry it will refresh when it is opened.
        stream.on("ListingStateChanged", (payload) => {
            void queryClient.invalidateQueries({
                queryKey: listingKey(novel, decodeImportKind(payload.kind)),
            });
        });

        stream.on("ImportItemFinished", (payload) => {
            activity.applyImportItem({
                jobId: payload.jobId,
                position: payload.position,
                sourceUrl: payload.sourceUrl,
                title: payload.title,
                chapterIndex: payload.chapterIndex,
                chapterId: payload.chapterId,
                state: decodeImportItemState(payload.state),
                status: payload.status,
                finishedAt: payload.finishedAt,
            });
        });

        void stream.watch(novel);
    }

    async function detach(): Promise<void> {
        await stream?.dispose();
        stream = null;
    }

    watch(
        () => toValue(novelId),
        (novel) => {
            void detach().then(() => {
                if (novel !== null) {
                    attach(novel);
                }
            });
        },
        { immediate: true },
    );

    onScopeDispose(() => {
        void detach();
    });
}
