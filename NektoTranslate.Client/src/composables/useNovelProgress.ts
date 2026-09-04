import type { MaybeRefOrGetter } from "vue";
import { computed } from "vue";

import { useChapters } from "./useChapters";


export interface NovelProgress {
    total: number;
    translated: number;
    failed: number;
    ratio: number;
}


// GET /api/novels now carries these counts itself, computed in SQL for the whole library in one
// query — see NovelRow, which reads them directly instead of calling this. This composable stays for
// the novel screen, where the full chapter list is already loaded for its own sake (the table, the
// search, the selection) and deriving the count from rows already in memory costs nothing further.
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
