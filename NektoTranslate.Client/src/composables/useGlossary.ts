import type { MaybeRefOrGetter } from "vue";
import type { UpsertGlossaryEntryRequest } from "@/types/api/requests";

import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
import { computed, toValue } from "vue";
import { glossaryApi } from "@/api";


export function glossaryKey(novelId: number): unknown[] {
    return ["novels", novelId, "glossary"];
}


// The whole glossary is fetched once and filtered in the browser. It is tens to low hundreds of
// entries even for a very long book, and the review filter is something the user flicks on and off
// while comparing — a round trip per flick would make it feel broken.
export function useGlossary(novelId: MaybeRefOrGetter<number | null>) {
    return useQuery({
        queryKey: computed(() => glossaryKey(toValue(novelId) as number)),
        queryFn: () => glossaryApi.list(toValue(novelId) as number),
        enabled: computed(() => toValue(novelId) !== null),
    });
}


export function useUpsertGlossaryEntry(novelId: MaybeRefOrGetter<number>) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (request: UpsertGlossaryEntryRequest) => glossaryApi.upsert(toValue(novelId), request),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: glossaryKey(toValue(novelId)) });
        },
    });
}


export function useApproveGlossaryEntry(novelId: MaybeRefOrGetter<number>) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (entryId: number) => glossaryApi.approve(toValue(novelId), entryId),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: glossaryKey(toValue(novelId)) });
        },
    });
}


export function useDeleteGlossaryEntry(novelId: MaybeRefOrGetter<number>) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (entryId: number) => glossaryApi.remove(toValue(novelId), entryId),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: glossaryKey(toValue(novelId)) });
        },
    });
}
