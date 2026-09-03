<template>
    <Transition name="strip">
        <div v-if="visible" class="strip" :class="tone">
            <UProgress
                class="track"
                size="2xs"
                :color="run.state === 'Paused' ? 'warning' : 'primary'"
                :model-value="run.total > 0 ? run.processed : null"
                :max="run.total > 0 ? run.total : 100"
                :get-value-label="progressLabel"
            />

            <div class="row">
                <span class="mark">
                    <ChapterStateDot v-if="run.isActive" state="Running" />
                    <UIcon v-else :name="settledIcon" class="settled-icon" />
                </span>

                <span class="headline">{{ headline }}</span>

                <span v-if="run.total > 0" class="counts">
                    {{ formatCount(run.processed) }} of {{ formatCount(run.total) }} chapters
                </span>

                <span class="spend">
                    {{ formatCost(run.costUsd) }}<template v-if="run.budgetUsd !== null">
                        of {{ formatCost(run.budgetUsd) }}
                    </template>
                </span>

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
            </div>
        </div>
    </Transition>
</template>

<script setup lang="ts">
    import { computed, ref } from "vue";

    import { jobsApi } from "@/api";
    import ChapterStateDot from "@/components/chapters/ChapterStateDot.vue";
    import { useRunStore } from "@/stores/run.store";
    import { formatCost, formatCount } from "@/utils/format";


    const run = useRunStore();
    const cancelling = ref(false);

    const visible = computed(() => run.state !== null);

    const tone = computed(() => {
        if (run.state === "Paused") {
            return "paused";
        }

        if (run.state === "Failed") {
            return "failed";
        }

        return run.isSettled ? "settled" : "running";
    });

    const settledIcon = computed(() => {
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
    });

    // Cancellation takes effect between chapters, so saying "Cancelled" the instant the button is
    // pressed would be a lie that makes the button look broken when the current chapter keeps going.
    const headline = computed(() => {
        if (cancelling.value && run.state === "Running") {
            return "Cancelling. The chapter already in flight will finish and be kept.";
        }

        if (run.state === "Paused") {
            return run.lastMessage ?? "Paused at the spending ceiling.";
        }

        if (run.isSettled) {
            return run.lastMessage ?? "Run finished.";
        }

        return run.currentChapterId === null ? "Preparing the run" : "Translating";
    });


    // Read out as chapters rather than as a percentage: "84 of 226 chapters" is what the user is
    // actually tracking, and it is the same sentence the strip shows sighted readers.
    function progressLabel(value: number | null | undefined, max: number): string {
        return value === null || value === undefined
            ? "Preparing the run"
            : `${formatCount(value)} of ${formatCount(max)} chapters translated`;
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
