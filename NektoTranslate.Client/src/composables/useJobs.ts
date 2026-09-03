import type { MaybeRefOrGetter } from "vue";
import type { StartTranslationJobRequest } from "@/types/api/requests";
import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";

import { computed, toValue } from "vue";
import { jobsApi } from "@/api";
import { useRunStore } from "@/stores/run.store";


export function jobsKey(novelId: number): unknown[] {
    return ["novels", novelId, "jobs"];
}


export function useJobs(novelId: MaybeRefOrGetter<number | null>) {
    return useQuery({
        queryKey: computed(() => jobsKey(toValue(novelId) as number)),
        queryFn: () => jobsApi.list(toValue(novelId) as number),
        enabled: computed(() => toValue(novelId) !== null),
    });
}


export function useStartJob(novelId: MaybeRefOrGetter<number>) {
    const queryClient = useQueryClient();
    const run = useRunStore();

    return useMutation({
        mutationFn: (request: StartTranslationJobRequest) => jobsApi.start(toValue(novelId), request),
        onSuccess: (job) => {
            run.adopt(job);
            void queryClient.invalidateQueries({ queryKey: jobsKey(toValue(novelId)) });
        },
    });
}


export function useCancelJob(novelId: MaybeRefOrGetter<number>) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (jobId: number) => jobsApi.cancel(toValue(novelId), jobId),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: jobsKey(toValue(novelId)) });
        },
    });
}
