import type { SpannableRow } from "./chapterSpan";

import { describe, expect, it } from "vitest";
import { chapterSpan } from "./chapterSpan";


// Ten chapters and a gap standing in for "chapter 4 is not in the book", the shape ChapterTable
// hands the helper once it has woven gap rows into the current order.
function rows(): SpannableRow[] {
    return [
        { id: "1", selectable: true },
        { id: "2", selectable: true },
        { id: "3", selectable: true },
        { id: "gap:3", selectable: false },
        { id: "5", selectable: true },
        { id: "6", selectable: true },
        { id: "7", selectable: true },
    ];
}


describe("chapterSpan", () => {
    it("closes over every selectable id between the two ends, inclusive", () => {
        expect(chapterSpan(rows(), "2", "6")).toEqual(["2", "3", "5", "6"]);
    });

    it("reads the same span whichever end is clicked first", () => {
        expect(chapterSpan(rows(), "6", "2")).toEqual(["2", "3", "5", "6"]);
    });

    it("is just the one id when both ends are the same row", () => {
        expect(chapterSpan(rows(), "3", "3")).toEqual(["3"]);
    });

    it("skips a gap row inside the span rather than breaking it", () => {
        expect(chapterSpan(rows(), "3", "5")).toEqual(["3", "5"]);
    });

    it("follows the table's current order, not the ids themselves", () => {
        // Newest-first: the same two chapters, the opposite slice.
        const newestFirst = [...rows()].reverse();

        expect(chapterSpan(newestFirst, "6", "2")).toEqual(["6", "5", "3", "2"]);
    });

    it("finds nothing when either end has left the table", () => {
        expect(chapterSpan(rows(), "1", "gone")).toEqual([]);
        expect(chapterSpan(rows(), "gone", "1")).toEqual([]);
    });
});
