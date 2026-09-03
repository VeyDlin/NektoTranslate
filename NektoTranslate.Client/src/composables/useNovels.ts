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
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ["novels"] });
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
