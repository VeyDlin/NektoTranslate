import type { MaybeRefOrGetter } from "vue";
import type { ImportedChapter, SetCurrentVersionsRequest } from "@/types/api/requests";
import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";

import { computed, toValue } from "vue";
import { chaptersApi, translationsApi } from "@/api";


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


// Pins one existing version current for one chapter - the reader's own "Make current" button. The
// server answers with the chapter detail whole, so the cache is replaced directly rather than
// refetched; the list is still invalidated for its currentIsOlder marker.
export function useMakeChapterCurrent(novelId: MaybeRefOrGetter<number>) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: ({ chapterId, translationId }: { chapterId: number; translationId: number }) =>
            chaptersApi.makeCurrent(toValue(novelId), chapterId, translationId),
        onSuccess: (chapter, { chapterId }) => {
            queryClient.setQueryData(chapterKey(toValue(novelId), chapterId), chapter);
            void queryClient.invalidateQueries({ queryKey: chaptersKey(toValue(novelId)) });
        },
    });
}


// The selection bar's bulk "use first/newest version" action.
export function useMakeVersionsCurrent(novelId: MaybeRefOrGetter<number>) {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (request: SetCurrentVersionsRequest) => translationsApi.setCurrentVersions(toValue(novelId), request),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: chaptersKey(toValue(novelId)) });
        },
    });
}
