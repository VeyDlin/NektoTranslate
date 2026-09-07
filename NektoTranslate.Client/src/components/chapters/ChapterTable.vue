<template>
    <UTable
        v-model:row-selection="selection"
        :data="tableRows"
        :columns="columns"
        :get-row-id="getRowId"
        :row-selection-options="rowSelectionOptions"
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
            <ChapterStateDot v-if="!isGapRow(row.original)" :state="row.original.translationState" />
        </template>

        <template #title-cell="{ row }">
            <span v-if="isGapRow(row.original)" class="gap">{{ gapSentence(row.original) }}</span>

            <RouterLink
                v-else
                class="title"
                :to="{ name: 'reader', params: { novelId, chapterId: row.original.id } }"
                :lang="scriptLangIf(row.original.title, scriptLang)"
            >
                {{ row.original.title }}
            </RouterLink>
        </template>

        <template #status-cell="{ row }">
            <span v-if="!isGapRow(row.original)" class="status">
                <span v-if="row.original.hasOriginal === false" class="no-original">no original</span>

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

    import { computed, h, ref, resolveComponent } from "vue";
    import { RouterLink } from "vue-router";
    import { chapterNumber } from "@/utils/format";
    import { scriptLangIf } from "@/utils/language";
    import { chapterSpan } from "./chapterSpan";
    import ChapterStateDot from "./ChapterStateDot.vue";


    // A gap stands for one or more chapter numbers the book has never had. It is drawn between the
    // real rows around it rather than stored anywhere, and carries only what the sentence needs.
    interface ChapterGapRow {
        gap: true;
        id: string;
        fromIndex: number;
        toIndex: number;
    }

    type ChapterRow = ChapterSummary | ChapterGapRow;


    const props = defineProps<{
        rows: ChapterSummary[];
        novelId: number;
        scriptLang?: string;
        showGaps: boolean;
    }>();


    function isGapRow(row: ChapterRow): row is ChapterGapRow {
        return "gap" in row;
    }


    // Keyed by chapter id rather than row position, so a selection survives the list being patched
    // by an incoming run event.
    const selection = defineModel<Record<string, boolean>>("selection", { required: true });

    const UCheckbox = resolveComponent("UCheckbox");

    // Gaps are a rendering of this table and nothing else. `props.rows` reaches StartRunModal, the
    // counts above the table and everything else exactly as it arrived; only what is handed to
    // UTable gets the synthetic rows woven in, and only once the caller has vouched (`showGaps`) that
    // the rows are the whole book — a filtered list would turn every chapter it left out into a
    // false gap.
    const tableRows = computed<ChapterRow[]>(() => (props.showGaps ? withGaps(props.rows) : props.rows));

    const rowSelectionOptions = {
        enableRowSelection: (row: { original: ChapterRow }) => !isGapRow(row.original),
    };

    // The row a checkbox was last clicked on - the anchor a Shift+click measures its span from. A
    // Shift+click moves it too, the same as a plain click, so a chain of Shift+clicks always measures
    // from wherever the pointer landed last rather than staying pinned to the very first one.
    const lastClickedId = ref<string | null>(null);

    // Whether the click being handled right now is holding Shift, caught on the way in: the checkbox
    // itself only ever reports the state it is taking, never how it was clicked, so this is read from
    // the DOM event's capture phase, just ahead of the checkbox's own listener turning it into an
    // `update:modelValue`.
    let shiftHeld = false;

    function onCheckboxClickCapture(event: MouseEvent): void {
        shiftHeld = event.shiftKey;
    }

    // A plain click toggles the one row clicked, as it always has. Shift+click instead sweeps every
    // selectable row between the anchor and this one - in the table's current order, gap rows skipped
    // - to the state this checkbox is taking. An anchor that no longer has a span to it (deleted since
    // it was clicked, or this being the very first click) falls back to the plain toggle.
    function toggleChapterRow(rowId: string, checked: boolean, toggleSelected: (value: boolean) => void): void {
        if (shiftHeld && lastClickedId.value !== null) {
            const span = chapterSpan(
                tableRows.value.map(candidate => ({ id: getRowId(candidate), selectable: !isGapRow(candidate) })),
                lastClickedId.value,
                rowId,
            );

            if (span.length > 0) {
                const next = { ...selection.value };

                for (const id of span) {
                    next[id] = checked;
                }

                selection.value = next;
                lastClickedId.value = rowId;

                return;
            }
        }

        toggleSelected(checked);
        lastClickedId.value = rowId;
    }

    const columns: TableColumn<ChapterRow>[] = [
        {
            id: "select",
            header: ({ table }) => h(UCheckbox, {
                "modelValue": table.getIsSomeRowsSelected() ? "indeterminate" : table.getIsAllRowsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => table.toggleAllRowsSelected(!!value),
                "aria-label": "Select every chapter",
            }),
            cell: ({ row }) => (isGapRow(row.original)
                ? null
                : h(
                    "span",
                    { onClickCapture: onCheckboxClickCapture },
                    [
                        h(UCheckbox, {
                            "modelValue": row.getIsSelected(),
                            "onUpdate:modelValue": (value: boolean | "indeterminate") => toggleChapterRow(
                                getRowId(row.original),
                                !!value,
                                checked => row.toggleSelected(checked),
                            ),
                            "aria-label": `Select chapter ${chapterNumber(row.original.index)}`,
                        }),
                    ],
                )),
            enableSorting: false,
            meta: { class: { th: "w-8", td: "w-8" } },
        },
        {
            id: "state",
            header: "",
            meta: { class: { th: "w-6", td: "w-6" } },
        },
        {
            id: "index",
            header: "#",
            cell: ({ row }) => (isGapRow(row.original) ? "" : chapterNumber(row.original.index)),
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
            tr: (row: { original: ChapterRow }) => (
                !isGapRow(row.original) && row.original.translationState === "Running" ? "bg-primary/5" : ""
            ),
        },
    };


    function getRowId(row: ChapterRow): string {
        return isGapRow(row) ? row.id : String(row.id);
    }


    // Walks the rows in the book's own order and turns every jump in the index sequence into one
    // synthetic row. The walk starts as though chapter zero had already been seen, so a leading gap
    // counts too: a book whose first chapter is numbered 4 opens with a gap for 1–3. There is no
    // check after the last chapter, so nothing here claims to know where the book ends.
    //
    // Newest-first hands the same book in reversed, so the walk is done on the ascending order and
    // the result turned around again - a gap between 3 and 7 is the same gap read from either end.
    function withGaps(chapters: ChapterSummary[]): ChapterRow[] {
        const first = chapters.at(0);
        const last = chapters.at(-1);
        const descending = first !== undefined && last !== undefined && first.index > last.index;
        const ascending = descending ? [...chapters].reverse() : chapters;
        const result: ChapterRow[] = [];
        let previousIndex: number | null = null;

        for (const chapter of ascending) {
            const fromIndex = previousIndex === null ? 0 : previousIndex + 1;

            if (chapter.index > fromIndex) {
                result.push(gapRow(fromIndex, chapter.index - 1));
            }

            result.push(chapter);
            previousIndex = chapter.index;
        }

        return descending ? result.reverse() : result;
    }


    function gapRow(fromIndex: number, toIndex: number): ChapterGapRow {
        return { gap: true, id: `gap:${fromIndex}`, fromIndex, toIndex };
    }


    // The same one-based numbering the rest of the table shows, because the missing chapters are
    // numbered exactly as they would have been had they arrived.
    function gapSentence(row: ChapterGapRow): string {
        const from = chapterNumber(row.fromIndex);
        const to = chapterNumber(row.toIndex);

        return from === to
            ? `Chapter ${from} is not in the book`
            : `Chapters ${from}–${to} are not in the book`;
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

    // The sentence a gap row carries in place of a title. Muted the same way the rest of the app
    // marks text that describes the list rather than being part of it.
    .gap {
        color: var(--ui-text-muted);
    }

    .status {
        display: inline-flex;
        align-items: center;
        justify-content: flex-end;
        gap: 0.625rem;

        // Styled like the alignment screen's own "no original" preview: muted, small, and set apart
        // by the same italic rather than by a colour that would fight with the note beside it.
        .no-original {
            font-size: var(--nt-text-sm);
            font-style: italic;
            color: var(--ui-text-muted);
        }

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
