<template>
    <div class="import-translation">
        <header class="bar">
            <UButton
                :to="{ name: 'novel', params: { novelId } }"
                icon="i-material-symbols:arrow-back-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                aria-label="Back to the chapter list"
            />

            <span class="book">{{ novel?.title ?? "" }}</span>

            <span class="where">Import an existing translation</span>

            <span class="spacer" />

            <UButton :to="{ name: 'alignment', params: { novelId } }" size="sm" color="neutral" variant="ghost">
                Alignment
            </UButton>

            <UColorModeButton size="sm" />
        </header>

        <div class="step">
            <UInput
                v-model="url"
                icon="i-material-symbols:link-rounded"
                placeholder="Address of the translation's contents page"
                size="sm"
                class="url"
                :disabled="jobActive"
                @keydown.enter="loadContents"
            />

            <UButton size="sm" :loading="isLoading" :disabled="url.trim() === '' || jobActive" @click="loadContents">
                Read the contents
            </UButton>

            <span v-if="support && !support.supported" class="unsupported">
                No parser claims that address.
            </span>

            <span v-else-if="support?.parser" class="supported">
                Read by <strong>{{ support.parser }}</strong>
            </span>
        </div>

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

            <p class="summary">{{ summary }}</p>

            <ul class="items">
                <li v-for="item in job.items ?? []" :key="item.position">
                    <span class="title">{{ item.title }}</span>

                    <ImportItemStateBadge :state="item.state" />

                    <span v-if="item.status && (item.state === 'Skipped' || item.state === 'Failed')" class="reason">
                        {{ describe(item.status) }}
                    </span>

                    <ImportItemRetryButton :novel-id="id" :job="job" :item="item" />
                </li>
            </ul>
        </div>

        <Transition name="pick">
            <div v-if="links.length > 0" class="picked">
                <span>{{ formatCount(selectedLinks.length) }} of {{ formatCount(links.length) }} chosen</span>

                <!-- The translation usually covers a run of chapters, not the whole book: entries one
                     to twenty when only those were translated by a human. -->
                <UInput
                    v-model="rangeSpec"
                    size="xs"
                    class="range"
                    placeholder="1-20"
                    aria-label="Entries to pick, written as 1-20"
                    :disabled="jobActive"
                    @keydown.enter="selectRange"
                />

                <UButton
                    size="xs"
                    color="neutral"
                    variant="ghost"
                    :disabled="jobActive || rangeSpec.trim() === ''"
                    @click="selectRange"
                >
                    Pick entries
                </UButton>

                <UButton size="xs" color="neutral" variant="ghost" :disabled="jobActive" @click="selectAll">
                    Select all
                </UButton>

                <UButton size="xs" color="neutral" variant="ghost" :disabled="jobActive" @click="rowSelection = {}">
                    Clear
                </UButton>

                <span class="gap" />

                <span class="starts">First chosen entry becomes chapter</span>

                <UInputNumber
                    v-model="startAtNumber"
                    :min="1"
                    :max="100000"
                    :disabled="jobActive"
                    size="sm"
                    class="start"
                />

                <span class="spacer" />

                <UButton
                    size="sm"
                    :disabled="selectedLinks.length === 0 || jobActive"
                    :loading="starting"
                    @click="runImport"
                >
                    Import {{ formatCount(selectedLinks.length) }} into {{ language }}
                </UButton>
            </div>
        </Transition>

        <!-- The one thing this screen exists to get right. Translation sites routinely open with a
             translator's note, so entry one is not chapter one, and every later chapter inherits the
             mistake. Showing the mapping before the import makes that visible while it is still
             free to fix. -->
        <p v-if="selectedLinks.length > 0" class="mapping">
            <strong>{{ firstTitle }}</strong> → chapter {{ startAtNumber }}
            <template v-if="selectedLinks.length > 1">
                &nbsp;·&nbsp;
                <strong>{{ lastTitle }}</strong> → chapter {{ startAtNumber + selectedLinks.length - 1 }}
            </template>
        </p>

        <!-- The case this screen could not hold until now: a translation of chapters the book has no
             original for. Refusing them is right when the offset is simply wrong, and wrong when the
             original does not exist anywhere — only the user knows which, so they are told the count
             and asked. -->
        <div v-if="missingCount > 0" class="orphans">
            <USwitch v-model="createMissing" />

            <span class="text">
                <strong>{{ formatCount(missingCount) }}</strong>
                {{ missingCount === 1 ? "entry lands on a chapter" : "entries land on chapters" }}
                the book does not have.
                <template v-if="createMissing">
                    {{ missingCount === 1 ? "It" : "They" }} will be created with no original —
                    readable and editable, but nothing to translate from.
                </template>
                <template v-else>
                    {{ missingCount === 1 ? "It" : "They" }} will be reported and skipped. Turn this
                    on if the original does not exist anywhere.
                </template>
            </span>
        </div>

        <UTable
            v-if="links.length > 0"
            v-model:row-selection="rowSelection"
            :data="links"
            :columns="columns"
            :get-row-id="getRowId"
            :virtualize="{ estimateSize: 40, overscan: 14 }"
            sticky
            class="flex-1 min-h-0"
            :ui="{ th: 'py-2 text-xs font-normal text-dimmed', td: 'py-0 h-10' }"
        />

        <div v-else-if="job === null" class="blank">
            <h1>Continue someone else's translation</h1>
            <p>
                Paste the contents page of a translation this book already has. It is stored beside
                the original exactly as our own output would be, so the glossary can read back what
                the previous translator called each character — which is what keeps chapter twenty-one
                using the names of chapters one to twenty.
            </p>
            <p>
                Nothing is ever overwritten. A chapter that already has a
                {{ language }} translation is reported and skipped.
            </p>
        </div>
    </div>
</template>

<script setup lang="ts">
    import type { TableColumn } from "@nuxt/ui";
    import type { ParserSupport } from "@/types/api/requests";
    import type { ParsedChapterLink } from "@/types/models/domain";

    import { computed, h, ref, resolveComponent } from "vue";
    import { parsingApi } from "@/api";
    import ImportItemRetryButton from "@/components/imports/ImportItemRetryButton.vue";
    import ImportItemStateBadge from "@/components/imports/ImportItemStateBadge.vue";
    import { useActivity, useCancelImport, usePauseImport, useResumeImport, useStartImport } from "@/composables/useActivity";
    import { useChapters } from "@/composables/useChapters";
    import { useNovel } from "@/composables/useNovels";
    import { useActivityStore } from "@/stores/activity.store";
    import { formatCount, importStateLabel } from "@/utils/format";
    import { parseRanges } from "@/utils/ranges";
    import { describe } from "@/utils/status";


    const props = defineProps<{ novelId: string }>();

    const id = computed(() => Number(props.novelId));

    useActivity(id);
    const activity = useActivityStore();

    const { data: novelData } = useNovel(id);

    // Shared with the novel screen's own query, so arriving here from the chapter list costs
    // nothing: the indices are already in the cache.
    const { data: chapterData } = useChapters(id);

    const novel = computed(() => novelData.value ?? null);
    const language = computed(() => novel.value?.targetLanguage ?? "");

    const url = ref("");
    const support = ref<ParserSupport | null>(null);
    const links = ref<ParsedChapterLink[]>([]);
    const rowSelection = ref<Record<string, boolean>>({});
    const rangeSpec = ref("");
    const isLoading = ref(false);
    const createMissing = ref(false);

    // Chapters are numbered from one everywhere the reader looks, so this field takes that number and
    // the 0-based index the API stores is derived at the call, not carried around the screen.
    const startAtNumber = ref(1);

    const { mutateAsync: startImport, isPending: starting } = useStartImport(id);
    const { mutateAsync: pauseImport, isPending: pausing } = usePauseImport(id);
    const { mutateAsync: resumeImport, isPending: resuming } = useResumeImport(id);
    const { mutateAsync: cancelImportJob, isPending: cancelling } = useCancelImport(id);

    const job = computed(() => activity.importFor("Translation"));

    const jobActive = computed(() => (
        job.value !== null
        && (job.value.state === "Queued" || job.value.state === "Running" || job.value.state === "Paused")
    ));

    const settled = computed(() => (
        job.value !== null
        && (job.value.state === "Completed" || job.value.state === "Failed" || job.value.state === "Cancelled")
    ));

    const UCheckbox = resolveComponent("UCheckbox");

    const columns: TableColumn<ParsedChapterLink>[] = [
        {
            id: "select",
            header: ({ table }) => h(UCheckbox, {
                "modelValue": table.getIsSomeRowsSelected() ? "indeterminate" : table.getIsAllRowsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => table.toggleAllRowsSelected(!!value),
                "disabled": jobActive.value,
                "aria-label": "Select every entry",
            }),
            cell: ({ row }) => h(UCheckbox, {
                "modelValue": row.getIsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => row.toggleSelected(!!value),
                "disabled": jobActive.value,
                "aria-label": `Select ${row.original.title}`,
            }),
            meta: { class: { th: "w-8", td: "w-8" } },
        },
        {
            accessorKey: "title",
            header: "Entry",
        },
    ];

    // Order matters and is the site's, not the click order: entries are mapped to consecutive
    // chapters in the order they appear in the contents.
    const selectedLinks = computed(() => links.value.filter(link => rowSelection.value[link.sourceUrl] === true));

    const firstTitle = computed(() => selectedLinks.value[0]?.title ?? "");
    const lastTitle = computed(() => selectedLinks.value[selectedLinks.value.length - 1]?.title ?? "");

    // Which chapters the book actually has, so the mapping can say how many of the chosen entries
    // have nowhere to land before the import runs rather than after it.
    const existingIndices = computed(() => new Set((chapterData.value ?? []).map(row => row.index)));

    const missingCount = computed(() => selectedLinks.value
        .filter((_, position) => !existingIndices.value.has(startAtNumber.value - 1 + position))
        .length);

    const summary = computed(() => {
        const items = job.value?.items ?? [];
        const imported = items.filter(item => item.state === "Imported").length;
        const skipped = items.filter(item => item.state === "Skipped").length;
        const failed = items.filter(item => item.state === "Failed").length;

        return `${formatCount(imported)} imported, ${formatCount(skipped)} skipped, ${formatCount(failed)} failed`;
    });


    function getRowId(link: ParsedChapterLink): string {
        return link.sourceUrl;
    }


    function selectAll(): void {
        rowSelection.value = Object.fromEntries(links.value.map(link => [link.sourceUrl, true]));
    }


    // Replaces the selection rather than adding to it: the field describes the whole pick, so typing
    // a second range after a first is a correction, not an addition.
    function selectRange(): void {
        const positions = parseRanges(rangeSpec.value, links.value.length);

        if (positions.length === 0) {
            return;
        }

        rowSelection.value = Object.fromEntries(positions.map(position => [links.value[position - 1].sourceUrl, true]));
    }


    // Support is checked first because it is answered from the host name alone. Telling the user the
    // address is unreadable costs nothing; finding out after a download does.
    async function loadContents(): Promise<void> {
        const address = url.value.trim();

        if (address === "") {
            return;
        }

        isLoading.value = true;

        try {
            support.value = await parsingApi.support(address);

            if (!support.value.supported) {
                links.value = [];
                return;
            }

            links.value = await parsingApi.tableOfContents(address);
            rowSelection.value = {};
        }
        finally {
            isLoading.value = false;
        }
    }


    async function runImport(): Promise<void> {
        await startImport({
            kind: "Translation",
            chapters: selectedLinks.value,
            language: language.value,
            startAtChapterIndex: startAtNumber.value - 1,
            createMissingChapters: createMissing.value,
        });

        rowSelection.value = {};
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
    @use "@/assets/scss/variables" as *;

    .import-translation {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;

        .bar {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.875rem;
            height: $chrome-height;
            padding: 0 1rem 0 0.5rem;
            border-bottom: 1px solid var(--ui-border);

            .book {
                color: var(--ui-text-muted);
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }

            .where {
                flex: none;
                color: var(--ui-text-highlighted);
            }

            .spacer {
                flex: 1;
            }
        }

        .step {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.75rem;
            padding: 0.75rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);

            .url {
                flex: 1;
                min-width: 0;
            }

            .unsupported {
                flex: none;
                color: var(--ui-warning);
            }

            .supported {
                flex: none;
                color: var(--ui-text-muted);
            }
        }

        .run {
            flex: none;
            display: flex;
            flex-direction: column;
            gap: 0.75rem;
            padding: 1rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);
            background: var(--ui-bg-elevated);

            .run-head {
                display: flex;
                align-items: center;
                gap: 1rem;

                .run-state {
                    color: var(--ui-text-muted);

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
                margin: 0;
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);
            }

            // Two thousand chapters imported in one job would otherwise print two thousand rows of
            // wall with no way to see the run panel above it.
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
                    display: flex;
                    align-items: center;
                    gap: 0.75rem;
                    padding: 0.5rem 0.75rem;

                    & + li {
                        border-top: 1px solid var(--ui-border);
                    }

                    .title {
                        flex: 1;
                        min-width: 0;
                        overflow: hidden;
                        text-overflow: ellipsis;
                        white-space: nowrap;
                    }

                    .reason {
                        flex: none;
                        max-width: 20rem;
                        overflow: hidden;
                        text-overflow: ellipsis;
                        white-space: nowrap;
                        font-size: var(--nt-text-sm);
                        color: var(--ui-text-muted);
                    }
                }
            }
        }

        .picked {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.75rem;
            padding: 0.5rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);
            background: var(--ui-bg-elevated);

            .range {
                width: 7rem;
            }

            .gap {
                width: 1rem;
            }

            .starts {
                color: var(--ui-text-muted);
                font-size: var(--nt-text-sm);
            }

            .start {
                width: 7rem;
            }

            .spacer {
                flex: 1;
            }
        }

        .orphans {
            flex: none;
            display: flex;
            align-items: flex-start;
            gap: 0.75rem;
            padding: 0.625rem 1.5rem 0.875rem;

            .text {
                max-width: 60rem;
                line-height: 1.5;
                color: var(--ui-text-muted);

                strong {
                    color: var(--ui-text-highlighted);
                }
            }
        }

        .mapping {
            flex: none;
            margin: 0;
            padding: 0.625rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);
            font-size: var(--nt-text-sm);
            color: var(--ui-text-muted);

            strong {
                color: var(--ui-text-highlighted);
                font-weight: 400;
            }
        }

        .blank {
            max-width: $reading-measure-comfortable;
            margin: 0 auto;
            padding: 5rem 1.5rem;

            h1 {
                margin: 0 0 0.75rem;
                font-family: var(--font-prose);
                font-size: var(--nt-text-xl);
                font-weight: 400;
                color: var(--ui-text-highlighted);
            }

            p {
                margin: 0 0 0.75rem;
                line-height: 1.6;
                color: var(--ui-text-muted);
            }
        }
    }

    .pick-enter-active,
    .pick-leave-active {
        transition: opacity 0.15s ease-out;
    }

    .pick-enter-from,
    .pick-leave-to {
        opacity: 0;
    }
</style>
