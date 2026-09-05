<template>
    <!-- Appears the moment a job of this kind exists — active, or settled and not yet dismissed —
         and stays through a navigate-away-and-back because it reads the store, not local state. -->
    <div v-if="job !== null" class="run">
        <div class="run-head">
            <span class="run-state" :class="job.state.toLowerCase()">{{ importStateLabel(job.state) }}</span>

            <span v-if="job.currentTitle" class="run-title">{{ job.currentTitle }}</span>

            <span v-if="job.totalCount > 0" class="run-counts">
                {{ formatCount(job.processedCount) }} of {{ formatCount(job.totalCount) }}
            </span>

            <span class="spacer" />

            <UButton
                v-if="job.state === 'Running'"
                size="xs"
                color="neutral"
                variant="ghost"
                :loading="pausing"
                @click="pause"
            >
                Pause
            </UButton>

            <UButton
                v-else-if="job.state === 'Paused'"
                size="xs"
                color="neutral"
                variant="ghost"
                :loading="resuming"
                @click="resume"
            >
                Resume
            </UButton>

            <UButton
                v-if="job.state === 'Running' || job.state === 'Paused'"
                size="xs"
                color="neutral"
                variant="ghost"
                :loading="cancelling"
                @click="cancelRun"
            >
                Cancel
            </UButton>

            <UButton
                v-else-if="settled"
                size="xs"
                color="neutral"
                variant="ghost"
                aria-label="Dismiss"
                icon="i-material-symbols:close-rounded"
                @click="dismiss"
            />
        </div>

        <UProgress
            size="2xs"
            :color="job.state === 'Paused' ? 'warning' : 'primary'"
            :model-value="job.totalCount > 0 ? job.processedCount : null"
            :max="job.totalCount > 0 ? job.totalCount : 100"
        />

        <!-- The counts double as the disclosure control. A per-chapter report of four hundred rows is
             worth having and not worth looking at most of the time, and the summary is the one line
             that says whether it is worth opening at all. -->
        <button type="button" class="summary" :aria-expanded="open" @click="open = !open">
            <UIcon
                :name="open
                    ? 'i-material-symbols:keyboard-arrow-down-rounded'
                    : 'i-material-symbols:keyboard-arrow-right-rounded'"
                class="chevron"
            />

            <span>{{ summary }}</span>
        </button>

        <ul v-if="open" class="items">
            <li v-for="item in job.items ?? []" :key="item.position">
                <div class="line">
                    <span class="title">{{ item.title }}</span>

                    <ImportItemStateBadge :state="item.state" />

                    <ImportItemRetryButton :novel-id="novelId" :job="job" :item="item" />
                </div>

                <!-- On its own line and free to wrap. Sharing the row with the title meant competing
                     for width with it, and the loser was the half that says what went wrong. -->
                <p v-if="reasonOf(item) !== null" class="reason">{{ reasonOf(item) }}</p>
            </li>
        </ul>
    </div>
</template>

<script setup lang="ts">
    import type { ImportJobItem, ImportKind } from "@/types/models/domain";

    import { computed, ref } from "vue";
    import { useCancelImport, usePauseImport, useResumeImport } from "@/composables/useActivity";
    import { useActivityStore } from "@/stores/activity.store";
    import { formatCount, importStateLabel } from "@/utils/format";
    import { describe } from "@/utils/status";
    import ImportItemRetryButton from "./ImportItemRetryButton.vue";
    import ImportItemStateBadge from "./ImportItemStateBadge.vue";


    // One panel for both import screens. They ran identical copies of this markup, which is how the
    // two of them drifted apart in the first place — a report that truncated its own error text on one
    // screen truncated it on the other, and each had to be found separately to fix it.
    const props = defineProps<{
        novelId: number;
        kind: ImportKind;
    }>();

    const activity = useActivityStore();

    const { mutateAsync: pauseImport, isPending: pausing } = usePauseImport(() => props.novelId);
    const { mutateAsync: resumeImport, isPending: resuming } = useResumeImport(() => props.novelId);
    const { mutateAsync: cancelImportJob, isPending: cancelling } = useCancelImport(() => props.novelId);

    const open = ref(true);

    const job = computed(() => activity.importFor(props.kind));

    const settled = computed(() => (
        job.value !== null
        && (job.value.state === "Completed" || job.value.state === "Failed" || job.value.state === "Cancelled")
    ));

    const summary = computed(() => {
        const items = job.value?.items ?? [];
        const imported = items.filter(item => item.state === "Imported").length;
        const skipped = items.filter(item => item.state === "Skipped").length;
        const failed = items.filter(item => item.state === "Failed").length;

        return `${formatCount(imported)} imported, ${formatCount(skipped)} skipped, ${formatCount(failed)} failed`;
    });


    // Only the outcomes that need explaining carry one. An imported chapter has nothing to say.
    function reasonOf(item: ImportJobItem): string | null {
        if (item.status === null || (item.state !== "Skipped" && item.state !== "Failed")) {
            return null;
        }

        return describe(item.status);
    }


    async function pause(): Promise<void> {
        if (job.value === null) {
            return;
        }

        await pauseImport(job.value.id);
    }


    async function resume(): Promise<void> {
        if (job.value === null) {
            return;
        }

        await resumeImport(job.value.id);
    }


    async function cancelRun(): Promise<void> {
        if (job.value === null) {
            return;
        }

        await cancelImportJob(job.value.id);
    }


    function dismiss(): void {
        if (job.value === null) {
            return;
        }

        activity.clearSettled(job.value.id);
    }
</script>

<style scoped lang="scss">
    .run {
        flex: none;
        display: flex;
        flex-direction: column;
        gap: 0.625rem;
        padding: 0.75rem 1.5rem;
        border-bottom: 1px solid var(--ui-border);

        .run-head {
            display: flex;
            align-items: center;
            gap: 0.75rem;

            .run-state {
                flex: none;

                &.running,
                &.queued {
                    color: var(--ui-primary);
                }

                &.paused {
                    color: var(--ui-warning);
                }

                &.failed {
                    color: var(--ui-error);
                }

                &.completed {
                    color: var(--ui-success);
                }
            }

            .run-title {
                flex: 1;
                min-width: 0;
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
                color: var(--ui-text-highlighted);
            }

            .run-counts {
                flex: none;
                color: var(--ui-text-muted);
            }

            .spacer {
                flex: 1;
            }
        }

        .summary {
            display: flex;
            align-items: center;
            gap: 0.25rem;
            align-self: flex-start;
            padding: 0;
            border: 0;
            background: none;
            font: inherit;
            font-size: var(--nt-text-sm);
            color: var(--ui-text-muted);
            cursor: pointer;

            &:hover {
                color: var(--ui-text-highlighted);
            }

            .chevron {
                width: 1rem;
                height: 1rem;
            }
        }

        // Capped and scrolled rather than allowed to grow: a four-hundred-chapter import would
        // otherwise push the picker below it off the screen entirely.
        .items {
            max-height: 22rem;
            overflow-y: auto;
            margin: 0;
            padding: 0;
            list-style: none;
            border: 1px solid var(--ui-border);
            border-radius: 0.375rem;
            background: var(--ui-bg);

            li {
                padding: 0.5rem 0.75rem;

                & + li {
                    border-top: 1px solid var(--ui-border);
                }

                .line {
                    display: flex;
                    align-items: center;
                    gap: 0.75rem;
                }

                .title {
                    flex: 1;
                    min-width: 0;
                    overflow: hidden;
                    text-overflow: ellipsis;
                    white-space: nowrap;
                }

                .reason {
                    margin: 0.25rem 0 0;
                    font-size: var(--nt-text-sm);
                    line-height: 1.45;
                    color: var(--ui-text-muted);
                }
            }
        }
    }
</style>
