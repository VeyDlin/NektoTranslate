<template>
    <UTable
        v-model:row-selection="selection"
        :data="rows"
        :columns="columns"
        :get-row-id="getRowId"
        :virtualize="{ estimateSize: 40, overscan: 14 }"
        :meta="meta"
        sticky
        empty="No chapters yet."
        class="flex-1 min-h-0"
        :ui="{
            th: 'py-2 text-xs font-normal text-dimmed',
            td: 'py-0 h-10',
            tr: 'data-[selected=true]:bg-elevated',
        }"
    >
        <template #state-cell="{ row }">
            <ChapterStateDot :state="row.original.translationState" />
        </template>

        <template #title-cell="{ row }">
            <RouterLink
                class="title"
                :to="{ name: 'reader', params: { novelId, chapterId: row.original.id } }"
                :lang="scriptLangIf(row.original.title, scriptLang)"
            >
                {{ row.original.title }}
            </RouterLink>
        </template>

        <template #status-cell="{ row }">
            <span class="status">
                <span v-if="noteFor(row.original)" class="note" :class="row.original.translationState.toLowerCase()">
                    {{ noteFor(row.original) }}
                </span>

                <UIcon
                    v-if="glossaryMark(row.original)"
                    :name="glossaryMark(row.original)!.icon"
                    :title="glossaryMark(row.original)!.title"
                    class="terms"
                />
            </span>
        </template>
    </UTable>
</template>

<script setup lang="ts">
    import type { TableColumn } from "@nuxt/ui";
    import type { ChapterSummary } from "@/types/models/domain";

    import { h, resolveComponent } from "vue";
    import { RouterLink } from "vue-router";
    import { chapterNumber } from "@/utils/format";
    import { scriptLangIf } from "@/utils/language";
    import ChapterStateDot from "./ChapterStateDot.vue";


    defineProps<{
        rows: ChapterSummary[];
        novelId: number;
        scriptLang?: string;
    }>();

    // Keyed by chapter id rather than row position, so a selection survives the list being patched
    // by an incoming run event.
    const selection = defineModel<Record<string, boolean>>("selection", { required: true });

    const UCheckbox = resolveComponent("UCheckbox");

    const columns: TableColumn<ChapterSummary>[] = [
        {
            id: "select",
            header: ({ table }) => h(UCheckbox, {
                "modelValue": table.getIsSomeRowsSelected() ? "indeterminate" : table.getIsAllRowsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => table.toggleAllRowsSelected(!!value),
                "aria-label": "Select every chapter",
            }),
            cell: ({ row }) => h(UCheckbox, {
                "modelValue": row.getIsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => row.toggleSelected(!!value),
                "aria-label": `Select chapter ${chapterNumber(row.original.index)}`,
            }),
            enableSorting: false,
            meta: { class: { th: "w-8", td: "w-8" } },
        },
        {
            id: "state",
            header: "",
            meta: { class: { th: "w-6", td: "w-6" } },
        },
        {
            accessorKey: "index",
            header: "#",
            cell: ({ row }) => chapterNumber(row.original.index),
            meta: { class: { th: "w-16 text-right", td: "w-16 text-right text-dimmed" } },
        },
        {
            id: "title",
            header: "Chapter",
        },
        {
            id: "status",
            header: "",
            meta: { class: { th: "w-40 text-right", td: "w-40 text-right" } },
        },
    ];

    // The chapter being worked on right now is the one thing on this screen moving by itself, so the
    // row carries the mark as well as the dot.
    //
    // The row parameter is typed structurally rather than as TanStack's `Row`. Nuxt UI does not
    // re-export the table types, and taking @tanstack/vue-table as a direct dependency to reach them
    // is what let npm hoist a v9 next to the v8 Nuxt UI is built on.
    const meta = {
        class: {
            tr: (row: { original: ChapterSummary }) => (
                row.original.translationState === "Running" ? "bg-primary/5" : ""
            ),
        },
    };


    function getRowId(row: ChapterSummary): string {
        return String(row.id);
    }


    // Glossary state is shown where it carries information, which is where it disagrees with the
    // translation state. Marking all 186 translated-and-analysed chapters puts an identical glyph
    // down the whole column and hides the two rows that are actually unusual — a chapter translated
    // without term extraction is the signature of an inherited third-party translation, and a
    // chapter analysed but not translated means the terms are banked and the prose is not.
    //
    // A literal reading of the brief would mark every analysed chapter. Say so if you want that back.
    function glossaryMark(row: ChapterSummary): { icon: string; title: string } | null {
        const translated = row.translationState === "Translated";
        const analyzed = row.glossaryState === "Analyzed";

        if (translated && !analyzed) {
            return {
                icon: "i-material-symbols:book-2-outline-rounded",
                title: "Translated, but its terms were never extracted",
            };
        }

        if (!translated && analyzed) {
            return {
                icon: "i-material-symbols:book-2-rounded",
                title: "Terms extracted, not translated yet",
            };
        }

        return null;
    }


    // A word appears only where a word is needed. Labelling every translated chapter "Translated"
    // in a list of two thousand buries the one that failed, which is the row the user is looking for.
    function noteFor(row: ChapterSummary): string | null {
        switch (row.translationState) {
            case "Failed":
                return "Failed";
            case "Running":
                return "Translating";
            case "Queued":
                return "Queued";
            default:
                return null;
        }
    }
</script>

<style scoped lang="scss">
    // Only the cell contents rendered through this component's slots. The table's own sizing lives
    // on the component root as Tailwind utilities, because a scoped rule cannot reach the wrapper
    // the virtualizer scrolls.
    .title {
        text-decoration: none;
        color: inherit;

        &:hover {
            color: var(--ui-primary);
        }
    }

    .status {
        display: inline-flex;
        align-items: center;
        justify-content: flex-end;
        gap: 0.625rem;

        .note {
            font-size: var(--nt-text-sm);

            &.failed {
                color: var(--ui-error);
            }

            &.running {
                color: var(--ui-primary);
            }

            &.queued {
                color: var(--ui-text-dimmed);
            }
        }

        .terms {
            width: 0.875rem;
            height: 0.875rem;
            color: var(--ui-text-dimmed);
        }
    }
</style>
