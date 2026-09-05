import type { MaybeRefOrGetter } from "vue";
import type { StartImportRequest } from "@/types/api/requests";
import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";

import { computed, toValue, watch } from "vue";
import { importsApi } from "@/api";
import { useActivityStore } from "@/stores/activity.store";
import { useRunStore } from "@/stores/run.store";
import { chaptersKey } from "./useChapters";


export function activityKey(novelId: number): unknown[] {
    return ["novels", novelId, "activity"];
}


// The one query a reloaded page needs: what is running for this novel right now. Feeds both stores —
// run.store for a translation in progress, activity.store for every live import — which is what lets
// either screen land back where it was instead of opening on a blank form.
export function useActivity(novelId: MaybeRefOrGetter<number | null>) {
    const run = useRunStore();
    const activity = useActivityStore();

    const query = useQuery({
        queryKey: computed(() => activityKey(toValue(novelId) as number)),
        queryFn: () => importsApi.activity(toValue(novelId) as number),
        enabled: computed(() => toValue(novelId) !== null),
    });

    watch(query.data, (loaded) => {
        if (loaded === undefined) {
            return;
        }

        if (loaded.translation !== null) {
            run.adopt(loaded.translation);
        }

        activity.adoptActivity(loaded);
    });

    return query;
}


export function useStartImport(novelId: MaybeRefOrGetter<number>) {
    const activity = useActivityStore();

    return useMutation({
        mutationFn: (request: StartImportRequest) => importsApi.start(toValue(novelId), request),
        onSuccess: (job) => {
            activity.adoptJob(job);
        },
    });
}


export function usePauseImport(novelId: MaybeRefOrGetter<number>) {
    return useMutation({
        mutationFn: (jobId: number) => importsApi.pause(toValue(novelId), jobId),
    });
}


export function useResumeImport(novelId: MaybeRefOrGetter<number>) {
    return useMutation({
        mutationFn: (jobId: number) => importsApi.resume(toValue(novelId), jobId),
    });
}


export function useCancelImport(novelId: MaybeRefOrGetter<number>) {
    return useMutation({
        mutationFn: (jobId: number) => importsApi.cancel(toValue(novelId), jobId),
    });
}


// Puts a settled run's report away on screen and on the server. The store forgets the job before the
// request goes out, so the panel closes on the click rather than a round trip later; the request is
// what keeps it closed after a reload.
export function useDismissImport(novelId: MaybeRefOrGetter<number>) {
    const activity = useActivityStore();

    return useMutation({
        mutationFn: (jobId: number) => {
            activity.clearSettled(jobId);

            return importsApi.dismiss(toValue(novelId), jobId);
        },
    });
}


// One row of a settled job's report, fetched and imported again. The store is patched directly from
// the response rather than waiting on the SignalR echo of it, so the badge on the row that was
// clicked updates the instant the request resolves instead of a round trip later — the same
// ImportItemFinished notification still arrives and applies the identical patch a second time.
export function useRetryImportItem(novelId: MaybeRefOrGetter<number>) {
    const activity = useActivityStore();
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (params: { jobId: number; position: number }) =>
            importsApi.retryItem(toValue(novelId), params.jobId, params.position),
        onSuccess: (item, params) => {
            activity.applyImportItem({ jobId: params.jobId, ...item, finishedAt: item.finishedAt ?? "" });

            // A retry that landed a chapter is the one case nothing else invalidates for: the run it
            // belongs to already settled, so ImportStateChanged will never fire again to trigger the
            // refetch the chapter list needs to show what just landed.
            if (item.state === "Imported") {
                void queryClient.invalidateQueries({ queryKey: chaptersKey(toValue(novelId)) });
            }
        },
    });
}
