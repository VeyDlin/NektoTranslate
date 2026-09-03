import type { MaybeRefOrGetter } from "vue";
import { computed } from "vue";

import { useChapters } from "./useChapters";


export interface NovelProgress {
    total: number;
    translated: number;
    failed: number;
    ratio: number;
}


// The novels endpoint returns the entity without its chapters loaded, so a count has to come from
// the chapter list. That list is body-less and shared with the novel screen's own query, which turns
// the cost into a prefetch — but it is still a two-thousand-row response fetched to render one
// number. Worth an added count on `GET /api/novels` when the backend next moves.
export function useNovelProgress(novelId: MaybeRefOrGetter<number | null>) {
    const { data, isLoading } = useChapters(novelId);

    const progress = computed<NovelProgress>(() => {
        const rows = data.value ?? [];
        const translated = rows.filter(row => row.translationState === "Translated").length;
        const failed = rows.filter(row => row.translationState === "Failed").length;

        return {
            total: rows.length,
            translated,
            failed,
            ratio: rows.length === 0 ? 0 : translated / rows.length,
        };
    });

    return { progress, isLoading };
}
