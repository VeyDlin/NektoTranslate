import type { ChapterTranslation } from "@/types/models/domain";

import { describe, expect, it } from "vitest";
import { bulkCurrentToastTitle, defaultShownTranslationId, sourceVersionPhrase } from "./translationVersions";


function translation(overrides: Partial<ChapterTranslation> = {}): ChapterTranslation {
    return {
        id: 1,
        language: "English",
        markdown: "text",
        origin: "Ai",
        costUsd: null,
        createdAt: "2026-01-01T00:00:00Z",
        isCurrent: false,
        ...overrides,
    };
}


describe("defaultShownTranslationId", () => {
    it("picks whichever translation is current, regardless of its position", () => {
        const translations = [
            translation({ id: 3, isCurrent: false }),
            translation({ id: 2, isCurrent: true }),
            translation({ id: 1, isCurrent: false }),
        ];

        expect(defaultShownTranslationId(translations)).toBe(2);
    });

    it("falls back to the first entry when nothing is flagged current", () => {
        const translations = [translation({ id: 5 }), translation({ id: 6 })];

        expect(defaultShownTranslationId(translations)).toBe(5);
    });

    it("is null when there are no translations at all", () => {
        expect(defaultShownTranslationId([])).toBeNull();
    });
});


describe("sourceVersionPhrase", () => {
    it("says nothing for Current", () => {
        expect(sourceVersionPhrase("Current")).toBe("");
    });

    it("names the first version", () => {
        expect(sourceVersionPhrase("First")).toBe("from the first version");
    });

    it("names the newest version", () => {
        expect(sourceVersionPhrase("Newest")).toBe("from the newest version");
    });
});


describe("bulkCurrentToastTitle", () => {
    it("reports the pick and the count with a skipped clause when something was skipped", () => {
        expect(bulkCurrentToastTitle("First", 12, 3)).toBe("First version is now current for 12 chapters, 3 skipped");
    });

    it("drops the skipped clause entirely when nothing was skipped", () => {
        expect(bulkCurrentToastTitle("Newest", 12, 0)).toBe("Newest version is now current for 12 chapters");
    });

    it("reads as one chapter, not as the number one", () => {
        expect(bulkCurrentToastTitle("First", 1, 0)).toBe("First version is now current for 1 chapter");
    });
});
