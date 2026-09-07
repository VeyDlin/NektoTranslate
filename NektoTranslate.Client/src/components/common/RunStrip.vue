<template>
    <Transition name="strip">
        <div v-if="visible" class="strip" :class="tone">
            <UProgress
                class="track"
                size="2xs"
                :color="tone === 'paused' ? 'warning' : 'primary'"
                :model-value="progressValue"
                :max="progressMax"
                :get-value-label="progressLabel"
            />

            <div class="row">
                <span class="mark">
                    <!-- The dot belongs to translation: its states are chapter states, and its label
                         says "Translating". An import in flight gets its own mark rather than a
                         borrowed one that reads wrong to a screen reader. -->
                    <ChapterStateDot v-if="isActive && run.state !== null" state="Running" />
                    <UIcon
                        v-else-if="isActive"
                        name="i-material-symbols:download-rounded"
                        class="settled-icon"
                        aria-label="Importing"
                    />
                    <UIcon v-else :name="settledIcon" class="settled-icon" />
                </span>

                <!-- The strip is the only thing on screen that knows a job is running, so it is also
                     the way back to the screen that is running it. Leaving that screen otherwise loses
                     the per-chapter report with no route back to it. -->
                <RouterLink v-if="importRoute !== null" class="headline link" :to="importRoute">
                    {{ headline }}
                </RouterLink>

                <span v-else class="headline">{{ headline }}</span>

                <span v-if="counts !== null" class="counts">{{ counts }}</span>

                <span v-if="run.state !== null" class="spend">
                    {{ formatCost(run.costUsd) }}<template v-if="run.budgetUsd !== null">
                        of {{ formatCost(run.budgetUsd) }}
                    </template>
                </span>

                <template v-if="run.state !== null">
                    <UButton
                        v-if="run.state === 'Running'"
                        size="xs"
                        color="neutral"
                        variant="ghost"
                        :loading="cancelling"
                        @click="cancel"
                    >
                        Cancel
                    </UButton>

                    <UButton
                        v-else-if="run.isSettled"
                        size="xs"
                        color="neutral"
                        variant="ghost"
                        aria-label="Dismiss"
                        icon="i-material-symbols:close-rounded"
                        @click="run.clear()"
                    />
                </template>

                <template v-else-if="activeImport !== null && !onOwningImportScreen">
                    <UButton
                        v-if="activeImport.state === 'Running'"
                        size="xs"
                        color="neutral"
                        variant="ghost"
                        :loading="importPausing"
                        @click="pauseImport"
                    >
                        Pause
                    </UButton>

                    <UButton
                        v-else-if="activeImport.state === 'Paused'"
                        size="xs"
                        color="neutral"
                        variant="ghost"
                        :loading="importResuming"
                        @click="resumeImport"
                    >
                        Resume
                    </UButton>

                    <UButton
                        v-if="activeImport.state === 'Running' || activeImport.state === 'Paused'"
                        size="xs"
                        color="neutral"
                        variant="ghost"
                        :loading="importCancelling"
                        @click="cancelImport"
                    >
                        Cancel
                    </UButton>
                </template>
            </div>
        </div>
    </Transition>
</template>

<script setup lang="ts">
    import type { RouteLocationRaw } from "vue-router";
    import type { ImportKind } from "@/types/models/domain";

    import { computed, ref } from "vue";
    import { RouterLink, useRoute } from "vue-router";

    import { importsApi, jobsApi } from "@/api";
    import ChapterStateDot from "@/components/chapters/ChapterStateDot.vue";
    import { useActivityStore } from "@/stores/activity.store";
    import { useRunStore } from "@/stores/run.store";
    import { formatCost, formatCount, jobModeProgressLabel, jobProgressUnit } from "@/utils/format";


    // The screen each import kind has to itself, where Pause/Resume/Cancel already live in its own
    // run panel. The strip still names what is running and links to it, but the buttons themselves
    // give way there rather than repeating next to a second, identical set.
    const IMPORT_OWNER_ROUTE: Record<ImportKind, string> = {
        Originals: "import-from-url",
        Translation: "import-translation",
    };

    const run = useRunStore();
    const activity = useActivityStore();
    const route = useRoute();

    const cancelling = ref(false);
    const importPausing = ref(false);
    const importResuming = ref(false);
    const importCancelling = ref(false);

    // A translation run always wins the strip: it is the one thing a novel does that the reader is
    // actively waiting on chapter by chapter. An import only shows here while nothing else does, and
    // only while it is still active — once it settles it drops out of `activeImports`, and the
    // dedicated import screen is where the finished run and its Dismiss control live on.
    const activeImport = computed(() => (run.state !== null ? null : activity.activeImports[0] ?? null));

    // True while the import's own screen is the one open — the route each kind owns, for the same
    // book the job belongs to.
    const onOwningImportScreen = computed(() => {
        if (activeImport.value === null) {
            return false;
        }

        return route.name === IMPORT_OWNER_ROUTE[activeImport.value.kind]
            && Number(route.params.novelId) === activeImport.value.novelId;
    });

    const visible = computed(() => run.state !== null || activeImport.value !== null);

    const importRoute = computed<RouteLocationRaw | null>(() => {
        if (activeImport.value === null) {
            return null;
        }

        return {
            name: activeImport.value.kind === "Translation" ? "import-translation" : "import-from-url",
            params: { novelId: activeImport.value.novelId },
        };
    });

    const tone = computed(() => {
        if (run.state !== null) {
            if (run.state === "Paused") {
                return "paused";
            }

            if (run.state === "Failed") {
                return "failed";
            }

            return run.isSettled ? "settled" : "running";
        }

        return activeImport.value?.state === "Paused" ? "paused" : "running";
    });

    const isActive = computed(() => {
        if (run.state !== null) {
            return run.isActive;
        }

        return activeImport.value !== null && activeImport.value.state !== "Paused";
    });

    const settledIcon = computed(() => {
        if (run.state !== null) {
            switch (run.state) {
                case "Paused":
                    return "i-material-symbols:pause-circle-outline";
                case "Failed":
                    return "i-material-symbols:error-outline-rounded";
                case "Cancelled":
                    return "i-material-symbols:cancel-outline-rounded";
                default:
                    return "i-material-symbols:check-circle-outline-rounded";
            }
        }

        // The only import state that reaches this icon (rather than the pulsing dot) is Paused —
        // Queued and Running count as active, and a settled import has already left `activeImports`.
        return "i-material-symbols:pause-circle-outline";
    });

    // Cancellation takes effect between chapters, so saying "Cancelled" the instant the button is
    // pressed would be a lie that makes the button look broken when the current chapter keeps going.
    const headline = computed(() => {
        if (run.state !== null) {
            if (cancelling.value && run.state === "Running") {
                return "Cancelling. The chapter already in flight will finish and be kept.";
            }

            if (run.state === "Paused") {
                return run.lastMessage ?? "Paused at the spending ceiling.";
            }

            if (run.isSettled) {
                return run.lastMessage ?? "Run finished.";
            }

            return run.currentStep === null
                ? jobModeProgressLabel(run.mode)
                : `${jobModeProgressLabel(run.mode)} — ${run.currentStep}`;
        }

        if (activeImport.value === null) {
            return "";
        }

        if (importCancelling.value && activeImport.value.state === "Running") {
            return "Cancelling. The chapter already in flight will finish and be kept.";
        }

        const verb = activeImport.value.state === "Paused" ? "Import paused" : "Importing";

        return activeImport.value.currentTitle === null ? verb : `${verb} — ${activeImport.value.currentTitle}`;
    });

    // Read out as chapters or steps rather than as a percentage: "84 of 226 chapters" is what the
    // user is actually tracking for Translate and Repair; LearnVoice has no chapter of its own to
    // move by, so its count is the steps - sample batches, then the learning passes over them.
    const counts = computed(() => {
        if (run.state !== null) {
            return run.total > 0
                ? `${formatCount(run.processed)} of ${formatCount(run.total)} ${jobProgressUnit(run.mode)}`
                : null;
        }

        if (activeImport.value !== null && activeImport.value.totalCount > 0) {
            return `${formatCount(activeImport.value.processedCount)} of ${formatCount(activeImport.value.totalCount)} chapters`;
        }

        return null;
    });

    // A translation run's own bar moves by the store's progress fraction rather than by whole
    // chapters, so a long chapter's batches move it too, not just the chapter finishing.
    const progressValue = computed(() => {
        if (run.state !== null) {
            return run.total > 0 ? run.progress : null;
        }

        return activeImport.value !== null && activeImport.value.totalCount > 0 ? activeImport.value.processedCount : null;
    });

    const progressMax = computed(() => {
        if (run.state !== null) {
            return run.total > 0 ? 1 : 100;
        }

        return activeImport.value !== null && activeImport.value.totalCount > 0 ? activeImport.value.totalCount : 100;
    });


    function progressLabel(value: number | null | undefined, max: number): string {
        if (value === null || value === undefined) {
            return "Preparing the run";
        }

        if (run.state !== null) {
            return `${formatCount(run.processed)} of ${formatCount(run.total)} ${jobProgressUnit(run.mode)} translated`;
        }

        return `${formatCount(value)} of ${formatCount(max)} chapters imported`;
    }


    async function cancel(): Promise<void> {
        if (run.novelId === null || run.jobId === null) {
            return;
        }

        cancelling.value = true;

        try {
            await jobsApi.cancel(run.novelId, run.jobId);
        }
        finally {
            cancelling.value = false;
        }
    }


    async function pauseImport(): Promise<void> {
        if (activeImport.value === null) {
            return;
        }

        importPausing.value = true;

        try {
            await importsApi.pause(activeImport.value.novelId, activeImport.value.id);
        }
        finally {
            importPausing.value = false;
        }
    }


    async function resumeImport(): Promise<void> {
        if (activeImport.value === null) {
            return;
        }

        importResuming.value = true;

        try {
            await importsApi.resume(activeImport.value.novelId, activeImport.value.id);
        }
        finally {
            importResuming.value = false;
        }
    }


    async function cancelImport(): Promise<void> {
        if (activeImport.value === null) {
            return;
        }

        importCancelling.value = true;

        try {
            await importsApi.cancel(activeImport.value.novelId, activeImport.value.id);
        }
        finally {
            importCancelling.value = false;
        }
    }
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .strip {
        position: relative;
        flex: none;
        height: $run-strip-height;
        border-top: 1px solid var(--ui-border);
        background: var(--ui-bg-elevated);

        .track {
            position: absolute;
            inset: 0 0 auto;
        }

        .row {
            display: flex;
            align-items: center;
            gap: 1.5rem;
            height: 100%;
            padding: 0 1rem;

            .mark {
                display: flex;
                align-items: center;
            }

            .settled-icon {
                width: 1rem;
                height: 1rem;
            }

            .headline {
                flex: 1;
                min-width: 0;
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
            }

            .link {
                text-decoration: none;
                color: inherit;

                &:hover {
                    color: var(--ui-primary);
                    text-decoration: underline;
                }
            }

            .counts,
            .spend {
                flex: none;
                color: var(--ui-text-muted);
            }
        }

        &.paused .settled-icon {
            color: var(--ui-warning);
        }

        &.failed .settled-icon {
            color: var(--ui-error);
        }

        &.settled .settled-icon {
            color: var(--ui-success);
        }
    }

    .strip-enter-active,
    .strip-leave-active {
        transition:
            transform 0.22s ease-out,
            opacity 0.22s ease-out;
    }

    .strip-enter-from,
    .strip-leave-to {
        transform: translateY(100%);
        opacity: 0;
    }
</style>
