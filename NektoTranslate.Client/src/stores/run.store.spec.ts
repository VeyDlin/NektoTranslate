import type { TranslationJob } from "@/types/models/domain";

import { createPinia, setActivePinia } from "pinia";
import { beforeEach, describe, expect, it } from "vitest";
import { useRunStore } from "./run.store";


// A run started from nothing but never adopted or reported: no step fields ever reach the store,
// so a caller building a job by hand does not have to fill them in.
function makeJob(overrides: Partial<TranslationJob> = {}): TranslationJob {
    return {
        id: 1,
        novelId: 1,
        mode: "Translate",
        scopeKind: "Range",
        fromIndex: 0,
        toIndex: 9,
        chapterIds: [],
        sourceVersion: "Current",
        state: "Running",
        processedCount: 3,
        totalCount: 10,
        costUsd: 0.5,
        budgetUsd: null,
        createdAt: "2026-01-01T00:00:00Z",
        startedAt: "2026-01-01T00:00:00Z",
        finishedAt: null,
        error: null,
        currentStep: null,
        stepIndex: null,
        stepCount: null,
        ...overrides,
    };
}


describe("useRunStore progress", () => {
    beforeEach(() => {
        setActivePinia(createPinia());
    });

    it("is the plain chapter fraction when there is no step to fold in", () => {
        const run = useRunStore();

        run.adopt(makeJob({ processedCount: 3, totalCount: 10, stepIndex: null, stepCount: null }));

        expect(run.progress).toBeCloseTo(0.3);
    });

    it("folds the current step's own fraction into the chapter it belongs to", () => {
        const run = useRunStore();

        // Chapter 4 of 10 done; the fifth is a third of the way through its own batches.
        run.adopt(makeJob({ processedCount: 4, totalCount: 10, stepIndex: 2, stepCount: 6 }));

        expect(run.progress).toBeCloseTo((4 + 2 / 6) / 10);
    });

    it("ignores a step fraction with a zero stepCount rather than dividing by it", () => {
        const run = useRunStore();

        run.adopt(makeJob({ processedCount: 2, totalCount: 5, stepIndex: 0, stepCount: 0 }));

        expect(run.progress).toBeCloseTo(2 / 5);
    });

    it("is zero while nothing has been scoped yet", () => {
        const run = useRunStore();

        run.adopt(makeJob({ processedCount: 0, totalCount: 0, stepIndex: null, stepCount: null }));

        expect(run.progress).toBe(0);
    });

    it("stays within 0 and 1 even if a step fraction would otherwise push it past the chapter", () => {
        const run = useRunStore();

        // Not a state a real run reaches, but the clamp is what keeps a bad report from drawing a
        // bar past full or into the negative rather than merely looking odd for one frame.
        run.adopt(makeJob({ processedCount: 10, totalCount: 10, stepIndex: 1, stepCount: 1 }));

        expect(run.progress).toBe(1);
    });

    it("carries the step fields through a live JobStateChanged event the same way adopt does", () => {
        const run = useRunStore();

        run.adopt(makeJob({ processedCount: 0, totalCount: 4 }));

        run.applyJobEvent({
            jobId: 1,
            state: "Running",
            processed: 1,
            total: 4,
            costUsd: 0.6,
            currentStep: "Chapter 2 · translating batch 3 of 5",
            stepIndex: 3,
            stepCount: 5,
        });

        expect(run.currentStep).toBe("Chapter 2 · translating batch 3 of 5");
        expect(run.progress).toBeCloseTo((1 + 3 / 5) / 4);
    });

    it("clears the step fields on reset", () => {
        const run = useRunStore();

        run.adopt(makeJob({ currentStep: "Aligning names", stepIndex: 1, stepCount: 3 }));
        run.clear();

        expect(run.currentStep).toBeNull();
        expect(run.stepIndex).toBeNull();
        expect(run.stepCount).toBeNull();
        expect(run.progress).toBe(0);
    });

    it("carries sourceVersion from the job that started the run", () => {
        const run = useRunStore();

        run.adopt(makeJob({ mode: "Repair", sourceVersion: "First" }));

        expect(run.sourceVersion).toBe("First");
    });

    it("resets sourceVersion to Current on clear", () => {
        const run = useRunStore();

        run.adopt(makeJob({ sourceVersion: "Newest" }));
        run.clear();

        expect(run.sourceVersion).toBe("Current");
    });
});
