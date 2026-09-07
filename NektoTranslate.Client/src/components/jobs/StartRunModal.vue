<template>
    <!-- While the request is out, nothing here can be changed or closed: the parameters it was sent
         with are the ones it will run with, and a dialog that lets them drift mid-flight would show a
         run that does not match what is on screen. -->
    <UModal v-model:open="open" :title="copy.title" :description="copy.description" :dismissible="!isPending">
        <template #body>
            <div class="run">
                <URadioGroup v-model="mode" :items="modeOptions" :disabled="isPending" />

                <URadioGroup v-model="scope" :items="scopeOptions" :disabled="isPending" />

                <div v-if="scope === 'Range'" class="range">
                    <UFormField label="From chapter">
                        <USelectMenu
                            v-model.nullable="fromChapterId"
                            :items="chapterOptions"
                            value-key="id"
                            label-key="label"
                            :virtualize="true"
                            :disabled="isPending"
                            class="w-full"
                        >
                            <template #item-label="{ item }">
                                <span class="chapter-option">
                                    <span class="position">{{ item.position }} ·</span>
                                    <span class="title">{{ item.title }}</span>
                                </span>
                            </template>
                        </USelectMenu>
                    </UFormField>

                    <UFormField label="To chapter">
                        <USelectMenu
                            v-model.nullable="toChapterId"
                            :items="chapterOptions"
                            value-key="id"
                            label-key="label"
                            :virtualize="true"
                            :disabled="isPending"
                            class="w-full"
                        >
                            <template #item-label="{ item }">
                                <span class="chapter-option">
                                    <span class="position">{{ item.position }} ·</span>
                                    <span class="title">{{ item.title }}</span>
                                </span>
                            </template>
                        </USelectMenu>
                    </UFormField>
                </div>

                <UFormField
                    label="Spending ceiling"
                    hint="Optional"
                    description="The run pauses when it reaches this. Nothing already paid for is lost."
                >
                    <UInputNumber
                        v-model="budget"
                        :min="0"
                        :step="0.5"
                        placeholder="No ceiling"
                        :disabled="isPending"
                        class="w-full"
                    />
                </UFormField>

                <div v-if="mode === 'Translate'" class="again">
                    <USwitch v-model="force" label="Re-translate with current settings" :disabled="isPending" />

                    <p class="detail">
                        Includes chapters that are already translated. Translations are cached against
                        the instructions that produced them, so this costs nothing and returns the same
                        text unless something has actually changed — the glossary, either set of style
                        notes, the quote setting or the model.
                    </p>
                </div>

                <div
                    v-if="mode === 'Translate' && translatedElsewhereCount > 0 && !voiceLoading && voiceProfile === null"
                    class="limit"
                >
                    <p>
                        {{ formatCount(translatedElsewhereCount) }}
                        {{ translatedElsewhereCount === 1 ? "chapter was" : "chapters were" }}
                        translated elsewhere and nothing has been learned from
                        {{ translatedElsewhereCount === 1 ? "it" : "them" }} yet.
                    </p>

                    <p class="detail">
                        Learn from {{ translatedElsewhereCount === 1 ? "it" : "them" }} first, so the
                        rest is translated in the same voice and with the same names.
                    </p>
                </div>

                <div v-if="mode === 'Repair'" class="limit">
                    <p>
                        Repair rewrites the existing translation on its own, with no original here to
                        check it against, and it also uses the glossary learned from chapters that have
                        their original. Names, terms, register and flow can be fixed; a fact the
                        translation already got wrong cannot, because nothing catches it.
                    </p>

                    <p v-if="!voiceLoading && voiceProfile === null" class="detail">
                        No voice has been learned for this book yet. Repair can still run, but without
                        one it has far less to imitate.
                    </p>
                </div>

                <div class="estimate">
                    <p class="headline">
                        <template v-if="targets.length === 0">
                            {{ copy.nothing }}
                        </template>
                        <template v-else>
                            {{ formatCount(targets.length) }}
                            {{ targets.length === 1 ? "chapter" : "chapters" }} will be {{ copy.verb }}.
                        </template>
                    </p>

                    <p v-if="skipped > 0" class="detail">
                        {{ formatCount(skipped) }}
                        {{ mode === 'Translate' ? "already translated and will be skipped." : "have no translation yet and will be skipped." }}
                    </p>

                    <p v-if="estimatedCost !== null && targets.length > 0" class="detail">
                        Roughly {{ formatCost(estimatedCost) }} at the rate of the chapters done so far.
                    </p>
                </div>

                <!-- Reserved whether or not there is anything to say, so a refusal landing here does
                     not move the buttons under the pointer about to press them. The dialog stays open
                     on a failure precisely so the parameters can be changed, and the reason has to
                     still be readable while they are - a toast would be gone by then. -->
                <p class="outcome" role="alert">{{ failure ?? "" }}</p>

                <div class="actions">
                    <UButton color="neutral" variant="ghost" :disabled="isPending" @click="open = false">
                        Cancel
                    </UButton>

                    <!-- The count on the button itself, where the click happens: a whole-book run
                         started for what was meant to be one chapter had only the small print above
                         to say so. -->
                    <UButton :disabled="targets.length === 0" :loading="isPending" @click="start">
                        {{ startLabel }}
                    </UButton>
                </div>
            </div>
        </template>
    </UModal>
</template>

<script setup lang="ts">
    import type { ChapterSummary, JobScopeKind, TranslationJobMode } from "@/types/models/domain";

    import { computed, ref, watch } from "vue";
    import { useStartJob } from "@/composables/useJobs";
    import { useVoiceProfile } from "@/composables/useVoice";
    import { describeFailure } from "@/utils/failure";
    import { chapterNumber, formatCost, formatCount, jobModeProgressLabel } from "@/utils/format";
    import { notifySuccess } from "@/utils/notify";


    const props = defineProps<{
        novelId: number;
        language: string;
        rows: ChapterSummary[];
        selectedIds: number[];
        averageCost: number | null;
    }>();

    const open = defineModel<boolean>("open", { required: true });
    const mode = defineModel<TranslationJobMode>("mode", { required: true });

    const { mutateAsync, isPending } = useStartJob(() => props.novelId);
    const { data: voiceProfileData, isLoading: voiceLoading } = useVoiceProfile(
        () => props.novelId,
        () => props.language,
    );

    const voiceProfile = computed(() => voiceProfileData.value ?? null);

    // "Translated elsewhere" is ChapterTable's glossaryMark signature for a chapter whose rendering
    // came from outside this book — translated, but never read for its terms. Translate mode is the
    // one place a book like that keeps drifting further from its own translator, so the hint below
    // only needs this count, not the chapters themselves.
    const translatedElsewhereCount = computed(() => props.rows.filter(row => (
        row.translationState === "Translated" && row.glossaryState === "NotAnalyzed"
    )).length);

    const scope = ref<JobScopeKind>("WholeBook");
    const budget = ref<number | undefined>(undefined);
    const force = ref(false);

    // The range is picked by which chapters, not by typing a position a reader has to work out for
    // themselves first — a book split across a site's own renumbering has no position that matches
    // what the book calls itself. Held as the chapter's id; the index the server wants is derived
    // from it below, so there is exactly one source of truth for which chapter each end names.
    const fromChapterId = ref<number | null>(null);
    const toChapterId = ref<number | null>(null);

    // One item per chapter of the book, in the order `rows` gives them. `label` carries both the
    // position and the title so the menu's own search - which matches against `labelKey` - finds a
    // chapter by either, with no separate filter to keep in step with what is actually shown.
    interface ChapterOption {
        id: number;
        position: number;
        title: string;
        label: string;
    }

    const chapterOptions = computed<ChapterOption[]>(() => props.rows.map(row => ({
        id: row.id,
        position: chapterNumber(row.index),
        title: row.title,
        label: `${chapterNumber(row.index)} · ${row.title}`,
    })));

    function chapterById(id: number | null): ChapterSummary | null {
        if (id === null) {
            return null;
        }

        return props.rows.find(row => row.id === id) ?? null;
    }

    // The row nearest either end of the book that has a translation - by its own index, not by
    // position in `rows`, so this holds even if the rows ever arrive in some other order.
    function extremeTranslatedChapter(direction: "earliest" | "latest"): ChapterSummary | null {
        return props.rows.filter(row => row.hasTranslation).reduce<ChapterSummary | null>((best, row) => {
            if (best === null) {
                return row;
            }

            const better = direction === "earliest" ? row.index < best.index : row.index > best.index;

            return better ? row : best;
        }, null);
    }

    // Whichever order the two ends were picked in, the run covers the span between them low to high
    // rather than refusing a "from" picked after a "to" - the reader's intent to cover that span is
    // clear either way. Every place that reads the range (the estimate, the button's count, the
    // request) goes through this, so the count on screen never disagrees with what Start actually
    // sends.
    const rangeBounds = computed(() => {
        const from = chapterById(fromChapterId.value);
        const to = chapterById(toChapterId.value);

        if (from === null || to === null) {
            return null;
        }

        return from.index <= to.index ? { from, to } : { from: to, to: from };
    });

    // What each kind of run is called, what it does, and how the estimate below reads for it. Kept
    // as one map rather than three separate switches, so a new mode only has one place to add copy
    // to instead of three chances to leave one behind.
    const MODE_COPY: Record<TranslationJobMode, { title: string; description: string; nothing: string; verb: string }> = {
        Translate: {
            title: "Translate chapters",
            description: "Chapters are translated in order, one at a time, so each one inherits the "
                + "glossary the one before it left.",
            nothing: "Nothing to do. Every chapter in that scope is already translated.",
            verb: "translated",
        },
        LearnVoice: {
            title: "Learn from the translation",
            description: "Reads the chapters in this range once, in order, and writes a profile of "
                + "how they are translated - register, names, formatting habits. Where a chapter "
                + "also has its original, it records how each name and term was rendered, so the "
                + "glossary starts from the translator's own choices rather than the model's. It "
                + "does not translate anything itself.",
            nothing: "Nothing to learn from. No chapter in that scope has a translation yet.",
            verb: "read to learn from",
        },
        Repair: {
            title: "Repair chapters",
            description: "Repairs each chapter in scope in order, one at a time, using the learned "
                + "voice where one exists.",
            nothing: "Nothing to repair. No chapter in that scope has a translation yet.",
            verb: "repaired",
        },
    };

    const copy = computed(() => MODE_COPY[mode.value]);

    // Repair needs nothing more than a chapter having a translation to work on; offering it over a
    // book that has none yet would be a choice with nowhere to point.
    const modeOptions = computed(() => {
        const options: { value: TranslationJobMode; label: string }[] = [
            { value: "Translate", label: "Translate" },
            { value: "LearnVoice", label: "Learn from the translation" },
        ];

        if (props.rows.some(row => row.hasTranslation)) {
            options.push({ value: "Repair", label: "Repair" });
        }

        return options;
    });

    // Learning a voice reads a contiguous stretch of the book, not a scattered pick — there is no
    // single range a handful of chapters from all over it would describe.
    const scopeOptions = computed(() => {
        const options = [
            { value: "WholeBook", label: "The whole book" },
            { value: "Range", label: "A range of chapters" },
        ];

        if (props.selectedIds.length > 0 && mode.value !== "LearnVoice") {
            options.push({
                value: "Selection",
                label: `The ${formatCount(props.selectedIds.length)} selected`,
            });
        }

        return options;
    });

    // Reloaded whenever the dialog opens, and again the moment the mode is switched inside an
    // already-open one — both are "starting fresh". Opened with chapters ticked, the run is about
    // those chapters: the whole book as the default there sent a 42-chapter repair for what was meant
    // to be one, with nothing but a line of small print to say so. Learn from the translation reads a
    // range instead, and the chapters that already carry a translation are the only sensible one.
    watch([open, mode], ([isOpen, current]) => {
        if (!isOpen) {
            return;
        }

        // Seeded once, the same way the numeric fields used to open on "1": a starting pair of ends
        // for whichever branch below does not set one of its own, so a reader who switches scope to
        // Range by hand always finds two real chapters already picked rather than two empty menus.
        if (fromChapterId.value === null || toChapterId.value === null) {
            const first = props.rows.at(0) ?? null;

            if (first !== null) {
                fromChapterId.value = first.id;
                toChapterId.value = first.id;
            }
        }

        if (current !== "LearnVoice") {
            if (props.selectedIds.length > 0) {
                scope.value = "Selection";
            }
            else if (current === "Repair") {
                // A repair of the whole book is the expensive choice and rarely the intended one.
                // It stays available, but the dialog opens on one chapter - the last that has a
                // rendering - for the reader to widen, rather than on forty-two to be narrowed.
                // Whatever the scope was before counts for nothing here: switching over from
                // learning left its whole-book range in place, which is forty-two again by another
                // door.
                const latest = extremeTranslatedChapter("latest");

                if (latest !== null) {
                    scope.value = "Range";
                    fromChapterId.value = latest.id;
                    toChapterId.value = latest.id;
                }
            }

            return;
        }

        if (scope.value === "Selection") {
            scope.value = "Range";
        }

        const earliest = extremeTranslatedChapter("earliest");
        const latest = extremeTranslatedChapter("latest");

        if (earliest !== null && latest !== null) {
            fromChapterId.value = earliest.id;
            toChapterId.value = latest.id;
        }
    });

    // Only translating needs an original: the server refuses a chapter without one for that mode, and
    // an estimate that counted it would promise work the run will not do. Learning and repairing read
    // the translation instead, and a chapter with no original is exactly what they exist for — a book
    // that arrived as somebody else's translation has nothing else in it.
    const inScope = computed(() => props.rows.filter(row => (
        mode.value !== "Translate" || row.hasOriginal
    )).filter((row) => {
        switch (scope.value) {
            case "Range":
                return rangeBounds.value !== null
                    && row.index >= rangeBounds.value.from.index
                    && row.index <= rangeBounds.value.to.index;
            case "Selection":
                return props.selectedIds.includes(row.id);
            default:
                return true;
        }
    }));

    // `totalCount` on the job is the number of chapters that will actually be worked on, not the
    // size of the range picked. Showing the same arithmetic before the run starts is what stops a
    // "translate the whole book" click from looking like it will cost forty dollars when it is a
    // top-up of the last twelve chapters. Learning and repairing both read from what is already
    // translated rather than adding to it, so their targets run the other way round from Translate's.
    const targets = computed(() => {
        if (mode.value === "Translate") {
            return force.value
                ? inScope.value
                : inScope.value.filter(row => row.translationState !== "Translated");
        }

        // A rendering on file is what repair and learning need, whatever the state says: a chapter
        // marked Failed by a broken repair still has the rendering it had before, and refusing to
        // repair it again would leave the failure the only thing that could ever happen to it.
        return inScope.value.filter(row => row.hasTranslation);
    });

    const skipped = computed(() => inScope.value.length - targets.value.length);

    // Voice learning is one call over a whole range rather than a per-chapter cost, and there is no
    // history of past repair runs to rate one against, so an estimate is only honest for Translate.
    const estimatedCost = computed(() => (
        mode.value === "Translate" && props.averageCost !== null ? props.averageCost * targets.value.length : null
    ));


    // The server's reason when a start was refused. Cleared the moment anything about the request
    // changes, because a sentence about the previous attempt would then be describing parameters
    // that are no longer on screen.
    const failure = ref<string | null>(null);

    watch([open, mode, scope, fromChapterId, toChapterId, budget, force], () => {
        failure.value = null;
    });

    // What the toast says once the run is on its way: the mode's own progress word and the scope,
    // so a reader who looks up from another screen knows which run just started and where to
    // watch it.
    const startedTitle = computed(() => {
        const scopeWords = scope.value === "Range" && rangeBounds.value !== null
            ? `chapters ${chapterNumber(rangeBounds.value.from.index)}–${chapterNumber(rangeBounds.value.to.index)}`
            : `${formatCount(targets.value.length)} ${targets.value.length === 1 ? "chapter" : "chapters"}`;

        return `${jobModeProgressLabel(mode.value)}: ${scopeWords}`;
    });


    const startLabel = computed(() => {
        const count = targets.value.length;
        const chapters = `${formatCount(count)} ${count === 1 ? "chapter" : "chapters"}`;

        switch (mode.value) {
            case "Translate":
                return `Translate ${chapters}`;
            case "Repair":
                return `Repair ${chapters}`;
            default:
                return `Learn from ${chapters}`;
        }
    });


    async function start(): Promise<void> {
        failure.value = null;

        try {
            await mutateAsync({
                mode: mode.value,
                scopeKind: scope.value,
                fromIndex: scope.value === "Range" ? rangeBounds.value?.from.index ?? null : null,
                toIndex: scope.value === "Range" ? rangeBounds.value?.to.index ?? null : null,
                chapterIds: scope.value === "Selection" ? [...props.selectedIds] : null,
                budgetUsd: budget.value ?? null,
                force: mode.value === "Translate" ? force.value : null,
            });
        }
        catch (error) {
            failure.value = describeFailure(error);

            return;
        }

        open.value = false;
        notifySuccess(startedTitle.value, "Progress is in the bar at the bottom of the screen; the report is in Runs.");
    }
</script>

<style scoped lang="scss">
    .run {
        display: flex;
        flex-direction: column;
        gap: 1.25rem;

        // `minmax(0, 1fr)` rather than `1fr`: a bare `1fr` column will not shrink below its content,
        // and the chapter pickers' content is a whole title - the second column was pushed clean
        // out of the dialog by the first. Zero as the floor lets the pickers truncate instead.
        .range {
            display: grid;
            grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
            gap: 1rem;
        }

        .again {
            display: flex;
            flex-direction: column;
            gap: 0.5rem;

            .detail {
                margin: 0;
                font-size: var(--nt-text-sm);
                line-height: 1.6;
                color: var(--ui-text-muted);
            }
        }

        // Stated plainly, at the same weight as the rest of the form, rather than folded into a
        // hint or a tooltip — this is a limit on what the run can do, not a footnote to it.
        .limit {
            padding: 0.875rem 1rem;
            border-left: 2px solid var(--ui-warning);

            p {
                margin: 0;
                line-height: 1.6;
                color: var(--ui-text-highlighted);
            }

            .detail {
                margin-top: 0.5rem;
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);
            }
        }

        .estimate {
            padding: 0.875rem 1rem;
            border-left: 2px solid var(--ui-border-accented);

            .headline {
                margin: 0;
                color: var(--ui-text-highlighted);
            }

            .detail {
                margin: 0.25rem 0 0;
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);
            }
        }

        // One line of room kept whether or not a refusal is showing, for the reason the comment in
        // the template gives: the buttons under it must not move.
        .outcome {
            min-height: 1.5rem;
            margin: 0;
            font-size: var(--nt-text-sm);
            line-height: 1.5;
            color: var(--ui-error);
        }

        .actions {
            display: flex;
            justify-content: flex-end;
            gap: 0.5rem;
        }
    }

    // Not nested under `.run`: the select menu's own list is teleported to a portal outside it, where
    // a descendant selector would never match.
    .chapter-option {
        display: flex;
        gap: 0.375rem;
        min-width: 0;

        .position {
            flex: none;
            color: var(--ui-text-muted);
        }

        .title {
            flex: 1 1 auto;
            min-width: 0;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }
    }
</style>
