import type { MaybeRefOrGetter } from "vue";
import type { TranslationStream } from "@/realtime/events";
import type { ChapterSummary } from "@/types/models/domain";

import { useQueryClient } from "@tanstack/vue-query";
import { onScopeDispose, toValue, watch } from "vue";
import { createTranslationStream } from "@/realtime/stream";

import { useRunStore } from "@/stores/run.store";
import { decodeJobState, decodeTranslationState } from "@/utils/wire";
import { chapterKey, chaptersKey } from "./useChapters";
import { glossaryKey } from "./useGlossary";
import { jobsKey } from "./useJobs";


// Binds one novel's event stream to the query cache and the run store.
//
// Chapter state is patched into the cached list rather than invalidating it. A run over nine
// hundred chapters emits two state changes per chapter; invalidating would refetch a
// two-thousand-row table eighteen hundred times, which is precisely the traffic the body-less list
// projection exists to avoid.
export function useTranslationStream(novelId: MaybeRefOrGetter<number | null>): void {
    const queryClient = useQueryClient();
    const run = useRunStore();

    let stream: TranslationStream | null = null;

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
            });

            void queryClient.invalidateQueries({ queryKey: jobsKey(novel) });
        });

        stream.on("GlossaryChanged", () => {
            void queryClient.invalidateQueries({ queryKey: glossaryKey(novel) });
        });

        stream.on("AgentMessage", (payload) => {
            run.pushMessage(payload.message);
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
