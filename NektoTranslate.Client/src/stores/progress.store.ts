import { defineStore } from "pinia";
import { ref } from "vue";


export interface ReadingPosition {
    chapterId: number;
    index: number;
    // The paragraph the reader had at the top of the page, counted from the chapter's first. A
    // paragraph rather than a pixel offset: it survives a different font size, a narrower window or
    // the other column of the bilingual view, none of which a scroll offset does. Absent on
    // positions saved before this was tracked, which read as the top of the chapter.
    block?: number;
    at: string;
}


// Where the reader stopped, per novel.
//
// This is the one piece of state that belongs to the person rather than to the book, and on a
// single-user machine that makes it a browser concern, not a server one — there is no second device
// to reconcile with and no second reader to keep apart.
export const useProgressStore = defineStore("progress", () => {
    const positions = ref<Record<string, ReadingPosition>>({});


    // Opening the chapter the reader left keeps the paragraph they left at; opening any other
    // chapter starts it from the top, so a stale paragraph from one chapter is never applied to the
    // next.
    function record(novelId: number, chapterId: number, index: number): void {
        const existing = positions.value[String(novelId)];
        const block = existing !== undefined && existing.chapterId === chapterId ? existing.block ?? 0 : 0;

        positions.value = {
            ...positions.value,
            [String(novelId)]: { chapterId, index, block, at: new Date().toISOString() },
        };
    }


    // Only for the chapter already on record: a scroll event that arrives late, from a chapter the
    // reader has just left, must not overwrite where they are in the one they moved to.
    function recordBlock(novelId: number, chapterId: number, block: number): void {
        const existing = positions.value[String(novelId)];

        if (existing === undefined || existing.chapterId !== chapterId || existing.block === block) {
            return;
        }

        positions.value = {
            ...positions.value,
            [String(novelId)]: { ...existing, block, at: new Date().toISOString() },
        };
    }


    function positionFor(novelId: number): ReadingPosition | null {
        return positions.value[String(novelId)] ?? null;
    }


    function forget(novelId: number): void {
        const next = { ...positions.value };

        delete next[String(novelId)];
        positions.value = next;
    }


    return { positions, record, recordBlock, positionFor, forget };
}, {
    persist: true,
});
