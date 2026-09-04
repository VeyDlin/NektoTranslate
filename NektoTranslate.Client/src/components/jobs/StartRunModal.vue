<template>
    <UModal v-model:open="open" title="Translate chapters" :description="sequentialNote">
        <template #body>
            <div class="run">
                <URadioGroup v-model="scope" :items="scopeOptions" />

                <div v-if="scope === 'Range'" class="range">
                    <UFormField label="From chapter">
                        <UInputNumber v-model="fromNumber" :min="1" :max="lastNumber" />
                    </UFormField>

                    <UFormField label="To chapter">
                        <UInputNumber v-model="toNumber" :min="1" :max="lastNumber" />
                    </UFormField>
                </div>

                <UFormField
                    label="Spending ceiling"
                    hint="Optional"
                    description="The run pauses when it reaches this. Nothing already paid for is lost."
                >
                    <UInputNumber v-model="budget" :min="0" :step="0.5" placeholder="No ceiling" />
                </UFormField>

                <div class="again">
                    <USwitch v-model="force" label="Re-translate with current settings" />

                    <p class="detail">
                        Includes chapters that are already translated. Translations are cached against
                        the instructions that produced them, so this costs nothing and returns the same
                        text unless something has actually changed — the glossary, either set of style
                        notes, the quote setting or the model.
                    </p>
                </div>

                <div class="estimate">
                    <p class="headline">
                        <template v-if="targets.length === 0">
                            Nothing to do. Every chapter in that scope is already translated.
                        </template>
                        <template v-else>
                            {{ formatCount(targets.length) }}
                            {{ targets.length === 1 ? "chapter" : "chapters" }} will be translated.
                        </template>
                    </p>

                    <p v-if="skipped > 0" class="detail">
                        {{ formatCount(skipped) }} already translated and will be skipped.
                    </p>

                    <p v-if="estimatedCost !== null && targets.length > 0" class="detail">
                        Roughly {{ formatCost(estimatedCost) }} at the rate of the chapters done so far.
                    </p>
                </div>

                <div class="actions">
                    <UButton color="neutral" variant="ghost" @click="open = false">
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
    import type { ChapterSummary, JobScopeKind } from "@/types/models/domain";

    import { computed, ref } from "vue";
    import { useStartJob } from "@/composables/useJobs";
    import { chapterNumber, formatCost, formatCount } from "@/utils/format";


    const props = defineProps<{
        novelId: number;
        rows: ChapterSummary[];
        selectedIds: number[];
        averageCost: number | null;
    }>();

    const open = defineModel<boolean>("open", { required: true });

    const { mutateAsync, isPending } = useStartJob(() => props.novelId);

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

    // Translation is strictly sequential: chapter N+1 is translated with the glossary as chapter N
    // left it. The dialog says so rather than offering an ordering or concurrency control the
    // backend would refuse to honour.
    const sequentialNote = "Chapters are translated in order, one at a time, so each one inherits the glossary the one before it left.";

    const scopeOptions = computed(() => {
        const options = [
            { value: "WholeBook", label: "The whole book" },
            { value: "Range", label: "A range of chapters" },
        ];

        if (props.selectedIds.length > 0) {
            options.push({
                value: "Selection",
                label: `The ${formatCount(props.selectedIds.length)} selected`,
            });
        }

        return options;
    });

    // A chapter with no original is out of every scope, force included — the server refuses it for
    // the same reason, and an estimate that counted it would promise work the run will not do.
    const inScope = computed(() => props.rows.filter(row => row.hasOriginal).filter((row) => {
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
    // top-up of the last twelve chapters.
    const targets = computed(() => (force.value
        ? inScope.value
        : inScope.value.filter(row => row.translationState !== "Translated")));

    const skipped = computed(() => inScope.value.length - targets.value.length);

    const estimatedCost = computed(() => (
        props.averageCost === null ? null : props.averageCost * targets.value.length
    ));


    async function start(): Promise<void> {
        await mutateAsync({
            scopeKind: scope.value,
            fromIndex: scope.value === "Range" ? fromNumber.value - 1 : null,
            toIndex: scope.value === "Range" ? toNumber.value - 1 : null,
            chapterIds: scope.value === "Selection" ? [...props.selectedIds] : null,
            budgetUsd: budget.value ?? null,
            force: force.value,
        });

        open.value = false;
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

        .actions {
            display: flex;
            justify-content: flex-end;
            gap: 0.5rem;
        }
    }
</style>
