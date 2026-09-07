import type { MaybeRefOrGetter } from "vue";
import type { CreateNovelRequest, UpdateNovelRequest } from "@/types/api/requests";
import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";

import { computed, toValue } from "vue";
import { novelsApi } from "@/api";


export function useNovels() {
    return useQuery({
        queryKey: ["novels"],
        queryFn: () => novelsApi.list(),
    });
}


export function useNovel(novelId: MaybeRefOrGetter<number | null>) {
    return useQuery({
        queryKey: computed(() => ["novels", toValue(novelId)]),
        queryFn: () => novelsApi.getById(toValue(novelId) as number),
        enabled: computed(() => toValue(novelId) !== null),
    });
}


export function useUpdateNovel(novelId: MaybeRefOrGetter<number>) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (data: UpdateNovelRequest) => novelsApi.update(toValue(novelId), data),
        onSuccess: (novel) => {
            queryClient.setQueryData(["novels", novel.id], novel);
            void queryClient.invalidateQueries({ queryKey: ["novels"] });
        },
    });
}


export function useDeleteNovel() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (novelId: number) => novelsApi.remove(novelId),
        onSuccess: (_result, novelId) => {
            // Exactly the list, not the prefix: the prefix covers the deleted book's own queries,
            // which are still mounted on the settings screen for the instant before it navigates
            // away, and refetching them would be a round of 404s for a book that is meant to be
            // gone. Its cache is dropped instead, so nothing ever asks after it again.
            queryClient.removeQueries({ queryKey: ["novels", novelId] });
            void queryClient.invalidateQueries({ queryKey: ["novels"], exact: true });
        },
    });
}


export function useCreateNovel() {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (data: CreateNovelRequest) => novelsApi.create(data),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ["novels"] });
        },
    });
}
