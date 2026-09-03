import type { MaybeRefOrGetter } from "vue";
import type { ImportedChapter } from "@/types/api/requests";
import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";

import { computed, toValue } from "vue";
import { chaptersApi } from "@/api";


export function chaptersKey(novelId: number): unknown[] {
    return ["novels", novelId, "chapters"];
}


export function chapterKey(novelId: number, chapterId: number): unknown[] {
    return ["novels", novelId, "chapters", chapterId];
}


export function useChapters(novelId: MaybeRefOrGetter<number | null>) {
    return useQuery({
        queryKey: computed(() => chaptersKey(toValue(novelId) as number)),
        queryFn: () => chaptersApi.list(toValue(novelId) as number),
        enabled: computed(() => toValue(novelId) !== null),
    });
}


export function useChapter(
    novelId: MaybeRefOrGetter<number | null>,
    chapterId: MaybeRefOrGetter<number | null>,
) {
    return useQuery({
        queryKey: computed(() => chapterKey(toValue(novelId) as number, toValue(chapterId) as number)),
        queryFn: () => chaptersApi.getById(toValue(novelId) as number, toValue(chapterId) as number),
        enabled: computed(() => toValue(novelId) !== null && toValue(chapterId) !== null),
    });
}


export function useDeleteChapter(novelId: MaybeRefOrGetter<number>) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (chapterId: number) => chaptersApi.remove(toValue(novelId), chapterId),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: chaptersKey(toValue(novelId)) });
        },
    });
}


export function useImportChapters(novelId: MaybeRefOrGetter<number>) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (chapters: ImportedChapter[]) => chaptersApi.import(toValue(novelId), chapters),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: chaptersKey(toValue(novelId)) });
        },
    });
}
