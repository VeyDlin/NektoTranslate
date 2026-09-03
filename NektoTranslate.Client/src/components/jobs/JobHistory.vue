<template>
    <UTable
        :data="jobs"
        :columns="columns"
        sticky
        empty="No runs yet. Start one from the chapter list."
        class="flex-1 min-h-0"
        :ui="{ th: 'py-2 text-xs font-normal text-dimmed' }"
    >
        <template #state-cell="{ row }">
            <span class="state" :class="row.original.state.toLowerCase()">
                {{ jobStateLabel(row.original.state) }}
            </span>
        </template>

        <template #scope-cell="{ row }">
            <span class="scope">
                <span>{{ scopeLabel(row.original) }}</span>

                <span v-if="row.original.error" class="reason error">{{ row.original.error }}</span>

                <span v-else-if="row.original.state === 'Paused'" class="reason">
                    Stopped at the {{ formatCost(row.original.budgetUsd) }} ceiling. Start another run to carry on.
                </span>
            </span>
        </template>
    </UTable>
</template>

<script setup lang="ts">
    import type { TableColumn } from "@nuxt/ui";
    import type { TranslationJob } from "@/types/models/domain";

    import { chapterNumber, formatCost, formatCount, formatWhen, jobStateLabel } from "@/utils/format";


    defineProps<{ jobs: TranslationJob[] }>();

    const columns: TableColumn<TranslationJob>[] = [
        {
            id: "state",
            header: "State",
            meta: { class: { th: "w-28", td: "w-28 align-top" } },
        },
        {
            id: "scope",
            header: "Scope",
            meta: { class: { td: "align-top" } },
        },
        {
            id: "progress",
            header: "Chapters",
            meta: { class: { th: "text-right", td: "text-right align-top text-muted" } },
            cell: ({ row }) => `${formatCount(row.original.processedCount)} of ${formatCount(row.original.totalCount)}`,
        },
        {
            id: "cost",
            header: "Spent",
            meta: { class: { th: "text-right", td: "text-right align-top text-muted" } },
            cell: ({ row }) => formatCost(row.original.costUsd),
        },
        {
            id: "when",
            header: "Started",
            meta: { class: { th: "w-28 text-right", td: "w-28 text-right align-top text-muted" } },
            cell: ({ row }) => formatWhen(row.original.createdAt),
        },
    ];


    function scopeLabel(job: TranslationJob): string {
        switch (job.scopeKind) {
            case "Range":
                return job.fromIndex === null || job.toIndex === null
                    ? "A range of chapters"
                    : `Chapters ${chapterNumber(job.fromIndex)} to ${chapterNumber(job.toIndex)}`;
            case "Single":
                return "One chapter";
            case "Selection":
                return `${formatCount(job.chapterIds.length)} picked chapters`;
            default:
                return "The whole book";
        }
    }
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    // `table` is a Tailwind display utility, so a class of that name on a component root collides
    // with it. Sizing lives on the root as utilities instead; these rules cover the slot contents.
    .state {
        &.running {
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

        &.cancelled,
        &.queued {
            color: var(--ui-text-muted);
        }
    }

    .scope {
        display: flex;
        flex-direction: column;
        gap: 0.25rem;

        .reason {
            max-width: $reading-measure-comfortable;
            font-size: var(--nt-text-sm);
            color: var(--ui-text-muted);

            &.error {
                color: var(--ui-error);
            }
        }
    }
</style>
