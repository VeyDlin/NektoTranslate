import type { ImportKind } from "@/types/models/domain";
import { defineStore } from "pinia";

import { ref } from "vue";


export interface ImportWorkbench {
    selectedUrls: string[];
    startAtNumber: number | null; // null until a pick sets it or the user types
    startAtTouched: boolean;
    createMissing: boolean;
    replaceExisting: boolean;
}


function keyFor(novelId: number, kind: ImportKind): string {
    return `${novelId}:${kind}`;
}


function defaultWorkbench(): ImportWorkbench {
    return {
        selectedUrls: [],
        startAtNumber: null,
        startAtTouched: false,
        createMissing: false,
        replaceExisting: false,
    };
}


// What the user has picked on an import screen, kept per novel and per kind so leaving the screen —
// on purpose, or by the back button while reaching for something else — does not throw it away.
// Checking twenty rows out of a listing of nine hundred is real work, and a reload used to cost every
// one of those clicks; a few bytes of local storage is a cheap trade for never asking again.
//
// `clearPick` only clears the pick itself - `selectedUrls`, the start chapter and whether it has been
// touched. `createMissing` and `replaceExisting` are left standing, because they are a decision about
// how this site's contents map onto this book, not about which rows happen to be checked right now:
// the next pick on the same site should not have to answer either question again.
export const useImportWorkbenchStore = defineStore("importWorkbench", () => {
    const entries = ref<Record<string, ImportWorkbench>>({});


    function bench(novelId: number, kind: ImportKind): ImportWorkbench {
        const key = keyFor(novelId, kind);

        if (entries.value[key] === undefined) {
            entries.value = { ...entries.value, [key]: defaultWorkbench() };
        }

        return entries.value[key];
    }


    function sameSelection(a: string[], b: string[]): boolean {
        return a.length === b.length && a.every((sourceUrl, index) => sourceUrl === b[index]);
    }


    function sameWorkbench(a: ImportWorkbench, b: ImportWorkbench): boolean {
        return a.startAtNumber === b.startAtNumber
            && a.startAtTouched === b.startAtTouched
            && a.createMissing === b.createMissing
            && a.replaceExisting === b.replaceExisting
            && sameSelection(a.selectedUrls, b.selectedUrls);
    }


    // A patch that would not actually change anything must leave `entries` untouched. Every screen
    // bound to an entry reads it through its own computed, and a fresh object written back with the
    // value it already held is indistinguishable, to those computeds, from a real change - which is
    // exactly what turned a table's own on-mount sync of its selection into an endless loop the first
    // time this was tried: the fresh object woke every dependent, one of which wrote straight back
    // here with the value it had just read.
    function patch(novelId: number, kind: ImportKind, partial: Partial<ImportWorkbench>): void {
        const key = keyFor(novelId, kind);
        const current = bench(novelId, kind);
        const next = { ...current, ...partial };

        if (sameWorkbench(current, next)) {
            return;
        }

        entries.value = { ...entries.value, [key]: next };
    }


    function clearPick(novelId: number, kind: ImportKind): void {
        patch(novelId, kind, { selectedUrls: [], startAtNumber: null, startAtTouched: false });
    }


    return { entries, bench, patch, clearPick };
}, {
    persist: true,
});
