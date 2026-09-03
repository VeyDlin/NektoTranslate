import { defineStore } from "pinia";
import { ref } from "vue";


export interface ReadingPosition {
    chapterId: number;
    index: number;
    at: string;
}


// Where the reader stopped, per novel.
//
// This is the one piece of state that belongs to the person rather than to the book, and on a
// single-user machine that makes it a browser concern, not a server one — there is no second device
// to reconcile with and no second reader to keep apart.
export const useProgressStore = defineStore("progress", () => {
    const positions = ref<Record<string, ReadingPosition>>({});


    function record(novelId: number, chapterId: number, index: number): void {
        positions.value = {
            ...positions.value,
            [String(novelId)]: { chapterId, index, at: new Date().toISOString() },
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


    return { positions, record, positionFor, forget };
}, {
    persist: true,
});
