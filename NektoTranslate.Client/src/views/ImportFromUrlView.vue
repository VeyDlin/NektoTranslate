<template>
    <div class="import-url">
        <header class="bar">
            <UButton
                :to="{ name: 'novel', params: { novelId } }"
                icon="i-material-symbols:arrow-back-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                aria-label="Back to the chapter list"
            />

            <span class="book" :lang="scriptLangIf(novel?.title ?? '', scriptLang)">{{ novel?.title ?? "" }}</span>

            <span class="where">Import from a site</span>

            <span class="spacer" />

            <UButton :to="{ name: 'parsers' }" size="sm" color="neutral" variant="ghost">
                Supported sites
            </UButton>

            <UColorModeButton size="sm" />
        </header>

        <div class="step">
            <UInput
                v-model="url"
                icon="i-material-symbols:link-rounded"
                placeholder="Address of the novel's contents page"
                size="sm"
                class="url"
                :disabled="addressLocked"
                @keydown.enter="read"
            />

            <UButton size="sm" :loading="readStarting" :disabled="!canRead" @click="read">
                {{ hasEntries ? "Read again" : "Read the contents" }}
            </UButton>

            <UButton
                v-if="reading"
                size="sm"
                color="neutral"
                variant="ghost"
                :loading="cancellingRead"
                @click="cancelRead"
            >
                Stop reading
            </UButton>
        </div>

        <!-- Always on screen, always the same height, and it names exactly one state. Both halves of
             that matter: a line that appears only sometimes is what made the row jump when the parser
             was identified, and a screen with no line at all is what made a read that takes a minute
             behind a one-tab-per-site queue look like nothing had happened. -->
        <p class="status" :class="statusTone">{{ statusText }}</p>

        <!-- Appears the moment a job of this kind exists — active, or settled and not yet dismissed —
             and stays through a navigate-away-and-back because it reads the store, not local state. -->
        <div v-if="job !== null" class="run">
            <div class="run-head">
                <span class="run-state" :class="job.state.toLowerCase()">{{ importStateLabel(job.state) }}</span>

                <span
                    v-if="job.currentTitle"
                    class="run-title"
                    :lang="scriptLangIf(job.currentTitle, scriptLang)"
                >
                    {{ job.currentTitle }}
                </span>

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
                    <span class="title" :lang="scriptLangIf(item.title, scriptLang)">{{ item.title }}</span>

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

                <!-- Chapters one to twenty out of nine hundred is the ordinary request, and twenty
                     clicks is not an answer to it. Rows are numbered as the site lists them. -->
                <UInput
                    v-model="rangeSpec"
                    size="xs"
                    class="range"
                    placeholder="1-20"
                    aria-label="Rows to pick, written as 1-20"
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
                    Pick rows
                </UButton>

                <UButton size="xs" color="neutral" variant="ghost" :disabled="jobActive" @click="selectAll">
                    Select all
                </UButton>

                <UButton size="xs" color="neutral" variant="ghost" :disabled="jobActive" @click="rowSelection = {}">
                    Clear
                </UButton>

                <span class="spacer" />

                <UButton
                    size="sm"
                    :disabled="selectedLinks.length === 0 || jobActive"
                    :loading="starting"
                    @click="runImport"
                >
                    Import {{ formatCount(selectedLinks.length) }}
                </UButton>
            </div>
        </Transition>

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
        >
            <template #title-cell="{ row }">
                <span :lang="scriptLangIf(row.original.title, scriptLang)">{{ row.original.title }}</span>
            </template>
        </UTable>

        <div v-else-if="job === null" class="blank">
            <h1>Nothing read yet</h1>
            <p>
                Paste the address of a novel's contents page. The chapter list is fetched first so you
                can choose what to bring in — a book with two thousand entries is never pulled down
                whole because a link was pasted.
            </p>
        </div>
    </div>
</template>

<script setup lang="ts">
    import type { TableColumn } from "@nuxt/ui";
    import type { ParsedChapterLink } from "@/types/models/domain";

    import { computed, h, ref, resolveComponent, watch } from "vue";
    import ImportItemRetryButton from "@/components/imports/ImportItemRetryButton.vue";
    import ImportItemStateBadge from "@/components/imports/ImportItemStateBadge.vue";
    import { useActivity, useCancelImport, usePauseImport, useResumeImport, useStartImport } from "@/composables/useActivity";
    import { useCancelListing, useListing, useReadListing } from "@/composables/useListing";
    import { useNovel } from "@/composables/useNovels";
    import { useActivityStore } from "@/stores/activity.store";
    import { formatCount, formatWhen, importStateLabel, siteLabel } from "@/utils/format";
    import { scriptLangFor, scriptLangIf } from "@/utils/language";
    import { parseRanges } from "@/utils/ranges";
    import { describe } from "@/utils/status";


    const props = defineProps<{ novelId: string }>();

    const id = computed(() => Number(props.novelId));

    useActivity(id);
    const activity = useActivityStore();

    const { data: novelData } = useNovel(id);

    const novel = computed(() => novelData.value ?? null);
    const scriptLang = computed(() => (novel.value === null ? undefined : scriptLangFor(novel.value.sourceLanguage)));

    // The contents this book has read from a site, as the server kept them. The address is seeded
    // from that listing rather than held separately, so the field and the table can never disagree
    // about which site is on screen — which is exactly how a refresh used to leave a filled address
    // above an empty table.
    const { data: listingData } = useListing(id, "Originals");
    const { mutateAsync: readListing, isPending: readStarting } = useReadListing(id, "Originals");
    const { mutateAsync: stopReading, isPending: cancellingRead } = useCancelListing(id, "Originals");

    const listing = computed(() => listingData.value ?? null);
    const links = computed(() => listing.value?.entries ?? []);
    const reading = computed(() => listing.value?.state === "Reading");
    const hasEntries = computed(() => links.value.length > 0);

    const url = ref("");
    const rowSelection = ref<Record<string, boolean>>({});
    const rangeSpec = ref("");

    const { mutateAsync: startImport, isPending: starting } = useStartImport(id);
    const { mutateAsync: pauseImport, isPending: pausing } = usePauseImport(id);
    const { mutateAsync: resumeImport, isPending: resuming } = useResumeImport(id);
    const { mutateAsync: cancelImportJob, isPending: cancelling } = useCancelImport(id);

    const job = computed(() => activity.importFor("Originals"));

    const jobActive = computed(() => (
        job.value !== null
        && (job.value.state === "Queued" || job.value.state === "Running" || job.value.state === "Paused")
    ));

    const settled = computed(() => (
        job.value !== null
        && (job.value.state === "Completed" || job.value.state === "Failed" || job.value.state === "Cancelled")
    ));

    // The address cannot be edited while the site is being read or while the chapters are being
    // imported. Locking the button alone left a field that accepted a new address the run behind it
    // was never going to use.
    const addressLocked = computed(() => reading.value || jobActive.value);

    const canRead = computed(() => url.value.trim() !== "" && !addressLocked.value && !readStarting.value);

    // One sentence for whichever state this screen is in. Every branch returns something, so the line
    // is never empty and the rows below it never move.
    const statusText = computed(() => {
        if (reading.value) {
            return `Reading the contents of ${siteLabel(listing.value?.url ?? url.value)}. This can take a minute, `
                + "and it keeps going if you leave this page.";
        }

        if (listing.value?.state === "Failed" && listing.value.error !== null) {
            return describe(listing.value.error);
        }

        if (listing.value?.state === "Ready") {
            const when = listing.value.readAt === null ? "" : `, read ${formatWhen(listing.value.readAt)}`;

            return `${formatCount(links.value.length)} chapters from ${siteLabel(listing.value.url)}${when}.`;
        }

        return "Paste the address of the contents page. Nothing is downloaded until you choose chapters.";
    });

    const statusTone = computed(() => {
        if (reading.value) {
            return "working";
        }

        return listing.value?.state === "Failed" ? "failed" : "";
    });

    const UCheckbox = resolveComponent("UCheckbox");

    const columns: TableColumn<ParsedChapterLink>[] = [
        {
            id: "select",
            header: ({ table }) => h(UCheckbox, {
                "modelValue": table.getIsSomeRowsSelected() ? "indeterminate" : table.getIsAllRowsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => table.toggleAllRowsSelected(!!value),
                "disabled": jobActive.value,
                "aria-label": "Select every chapter",
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
            id: "title",
            header: "Chapter",
        },
    ];

    const selectedLinks = computed(() => links.value.filter(link => rowSelection.value[link.sourceUrl] === true));

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
    // Starts the read and returns; the site is visited on the server. Whether the address is readable
    // at all is answered there too, as the read's own outcome, rather than by a second call here -
    // one question with one answer is what makes the state on screen unambiguous.
    async function read(): Promise<void> {
        const address = url.value.trim();

        if (address === "") {
            return;
        }

        rowSelection.value = {};
        await readListing(address);
    }


    async function cancelRead(): Promise<void> {
        await stopReading();
    }


    // The user already gave this address once, when creating the book — asking again on the very
    // next screen was the complaint. Guarded by `url` being empty so it only ever fires once, the
    // first time the novel's own address arrives.
    // Seeded, never read automatically. Arriving here used to visit the site on its own, which was a
    // free-looking convenience only until the listing was kept: now a return visit already has the
    // entries, and re-reading every time would mean a page load the user did not ask for.
    watch([novel, listing], ([loadedNovel, loadedListing]) => {
        if (url.value.trim() !== "") {
            return;
        }

        if (loadedListing !== null) {
            url.value = loadedListing.url;

            return;
        }

        if (loadedNovel?.sourceUrl != null) {
            url.value = loadedNovel.sourceUrl;
        }
    }, { immediate: true });


    async function runImport(): Promise<void> {
        await startImport({ kind: "Originals", chapters: selectedLinks.value });
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

    .import-url {
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

        }

        // Fixed height on purpose. This line changes what it says, never whether it is there, so
        // nothing below it ever moves because the screen changed state.
        .status {
            flex: none;
            height: 2.5rem;
            display: flex;
            align-items: center;
            margin: 0;
            padding: 0 1.5rem;
            border-bottom: 1px solid var(--ui-border);
            color: var(--ui-text-muted);

            &.working {
                color: var(--ui-primary);
            }

            &.failed {
                color: var(--ui-error);
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

            .spacer {
                flex: 1;
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
                margin: 0;
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
