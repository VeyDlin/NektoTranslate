import type { MaybeRefOrGetter } from "vue";
import type { StartImportRequest } from "@/types/api/requests";
import { useMutation, useQuery } from "@tanstack/vue-query";

import { computed, toValue, watch } from "vue";
import { importsApi } from "@/api";
import { useActivityStore } from "@/stores/activity.store";
import { useRunStore } from "@/stores/run.store";


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
