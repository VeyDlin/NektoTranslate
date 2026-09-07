import type { JobState, TranslationJob, TranslationJobMode } from "@/types/models/domain";
import { defineStore } from "pinia";

import { computed, ref } from "vue";


// Live run state arrives by push, not by query, so it lives here rather than in vue-query. It is
// also deliberately global: the run strip has to stay visible while the user walks off to read a
// chapter, which is the whole point of the strip.
export const useRunStore = defineStore("run", () => {
    const novelId = ref<number | null>(null);
    const jobId = ref<number | null>(null);

    // Set once from the job that started the run and never touched again: what a run does does not
    // change while it is happening, unlike its state, so there is nothing for a job event to update
    // it with.
    const mode = ref<TranslationJobMode>("Translate");

    const state = ref<JobState | null>(null);
    const processed = ref(0);
    const total = ref(0);
    const costUsd = ref(0);
    const budgetUsd = ref<number | null>(null);
    const currentChapterId = ref<number | null>(null);
    const messages = ref<string[]>([]);

    // What the run is doing right now, and where inside its current unit of work - a batch inside
    // the chapter being translated or repaired for Translate/Repair, nothing for LearnVoice, whose
    // steps move processed/total directly instead.
    const currentStep = ref<string | null>(null);
    const stepIndex = ref<number | null>(null);
    const stepCount = ref<number | null>(null);

    // Prose accumulating for the chapter being written right now, keyed by chapter. Lives here
    // rather than in the query cache because it is not the stored translation yet — it is a chapter
    // mid-sentence, and it is discarded the moment the finished one arrives.
    const deltas = ref<Record<number, string>>({});

    const isActive = computed(() => state.value === "Running" || state.value === "Queued");

    const isSettled = computed(() => (
        state.value === "Completed" || state.value === "Cancelled" || state.value === "Failed"
    ));

    // A fraction of the way through the current chapter, folded into the chapter count itself so the
    // bar moves batch by batch instead of jumping once per chapter. Only meaningful for Translate and
    // Repair, where stepIndex/stepCount describe a batch inside processed.value's own chapter;
    // LearnVoice never sets them, so its bar already moves on every step through processed/total
    // alone and this fraction stays zero.
    const progress = computed(() => {
        if (total.value === 0) {
            return 0;
        }

        const fraction = stepIndex.value !== null && stepCount.value !== null && stepCount.value > 0
            ? stepIndex.value / stepCount.value
            : 0;

        return Math.min(1, Math.max(0, (processed.value + fraction) / total.value));
    });

    const lastMessage = computed(() => messages.value.at(-1) ?? null);


    function adopt(job: TranslationJob): void {
        novelId.value = job.novelId;
        jobId.value = job.id;
        mode.value = job.mode;
        state.value = job.state;
        processed.value = job.processedCount;
        total.value = job.totalCount;
        costUsd.value = job.costUsd;
        budgetUsd.value = job.budgetUsd;
        currentChapterId.value = null;
        messages.value = [];
        currentStep.value = job.currentStep;
        stepIndex.value = job.stepIndex;
        stepCount.value = job.stepCount;
    }


    function applyJobEvent(payload: {
        jobId: number;
        state: JobState;
        processed: number;
        total: number;
        costUsd: number;
        currentStep: string | null;
        stepIndex: number | null;
        stepCount: number | null;
    }): void {
        jobId.value = payload.jobId;
        state.value = payload.state;
        processed.value = payload.processed;
        total.value = payload.total;
        costUsd.value = payload.costUsd;
        currentStep.value = payload.currentStep;
        stepIndex.value = payload.stepIndex;
        stepCount.value = payload.stepCount;
    }


    function setCurrentChapter(chapterId: number | null): void {
        currentChapterId.value = chapterId;
    }


    function appendDelta(chapterId: number, text: string): void {
        deltas.value = {
            ...deltas.value,
            [chapterId]: (deltas.value[chapterId] ?? "") + text,
        };
    }


    // Dropped as soon as the stored chapter lands, so the reader never shows a half-written draft
    // beside the finished text it belongs to.
    function clearDelta(chapterId: number): void {
        const next = { ...deltas.value };

        delete next[chapterId];
        deltas.value = next;
    }


    function pushMessage(message: string): void {
        messages.value = [...messages.value, message].slice(-20);
    }


    function clear(): void {
        novelId.value = null;
        jobId.value = null;
        mode.value = "Translate";
        state.value = null;
        processed.value = 0;
        total.value = 0;
        costUsd.value = 0;
        budgetUsd.value = null;
        currentChapterId.value = null;
        messages.value = [];
        deltas.value = {};
        currentStep.value = null;
        stepIndex.value = null;
        stepCount.value = null;
    }


    return {
        novelId,
        jobId,
        mode,
        state,
        processed,
        total,
        costUsd,
        budgetUsd,
        currentChapterId,
        messages,
        deltas,
        currentStep,
        stepIndex,
        stepCount,
        isActive,
        isSettled,
        progress,
        lastMessage,
        adopt,
        applyJobEvent,
        setCurrentChapter,
        appendDelta,
        clearDelta,
        pushMessage,
        clear,
    };
});
