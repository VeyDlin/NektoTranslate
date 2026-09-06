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
                        <UInputNumber v-model="fromNumber" :min="1" :max="lastNumber" :disabled="isPending" />
                    </UFormField>

                    <UFormField label="To chapter">
                        <UInputNumber v-model="toNumber" :min="1" :max="lastNumber" :disabled="isPending" />
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

                    <UButton :disabled="targets.length === 0" :loading="isPending" @click="start">
                        Start
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

    // Both ends are held as the numbers the reader sees, which start at one, and converted back to
    // the server's zero-based index only when the job is sent. Held the other way round, a minimum
    // of one would put the first chapter of the book permanently out of reach of a range.
    const fromNumber = ref(1);
    const toNumber = ref(1);

    const lastNumber = computed(() => props.rows.reduce(
        (highest, row) => Math.max(highest, chapterNumber(row.index)),
        1,
    ));

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

        if (props.rows.some(row => row.translationState === "Translated")) {
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

    // Reloaded whenever the dialog opens on Learn from the translation, and again the moment it is
    // switched to from inside an already-open one — both are "starting fresh", and the chapters
    // that already carry a translation are the only sensible default range to read a voice from.
    watch([open, mode], ([isOpen, current]) => {
        if (!isOpen || current !== "LearnVoice") {
            return;
        }

        if (scope.value === "Selection") {
            scope.value = "Range";
        }

        const translated = props.rows
            .filter(row => row.translationState === "Translated")
            .map(row => chapterNumber(row.index));

        if (translated.length > 0) {
            fromNumber.value = Math.min(...translated);
            toNumber.value = Math.max(...translated);
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
                return chapterNumber(row.index) >= fromNumber.value
                    && chapterNumber(row.index) <= toNumber.value;
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

        return inScope.value.filter(row => row.translationState === "Translated");
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

    watch([open, mode, scope, fromNumber, toNumber, budget, force], () => {
        failure.value = null;
    });

    // What the toast says once the run is on its way: the mode's own progress word and the scope,
    // so a reader who looks up from another screen knows which run just started and where to
    // watch it.
    const startedTitle = computed(() => {
        const scopeWords = scope.value === "Range"
            ? `chapters ${fromNumber.value}–${toNumber.value}`
            : `${formatCount(targets.value.length)} ${targets.value.length === 1 ? "chapter" : "chapters"}`;

        return `${jobModeProgressLabel(mode.value)}: ${scopeWords}`;
    });


    async function start(): Promise<void> {
        failure.value = null;

        try {
            await mutateAsync({
                mode: mode.value,
                scopeKind: scope.value,
                fromIndex: scope.value === "Range" ? fromNumber.value - 1 : null,
                toIndex: scope.value === "Range" ? toNumber.value - 1 : null,
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

        .range {
            display: grid;
            grid-template-columns: 1fr 1fr;
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
</style>
