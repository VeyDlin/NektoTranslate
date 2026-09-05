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
                :disabled="addressLocked"
                @keydown.enter="read"
            />

            <!-- One button that changes what it does, not a second one that appears beside it. A
                 control that shows up only while something runs is a control that moves everything
                 around it the moment the run starts, and the button that began the read is where a
                 hand already is when it wants to stop it. The width is fixed so that the label
                 changing does not move the field either. -->
            <UButton
                size="sm"
                class="read"
                :color="reading ? 'neutral' : 'primary'"
                :variant="reading ? 'subtle' : 'solid'"
                :loading="readStarting || cancellingRead"
                :disabled="!canPressRead"
                @click="reading ? cancelRead() : read()"
            >
                {{ readLabel }}
            </UButton>
        </div>

        <!-- Always on screen, always the same height, and it names exactly one state. Both halves of
             that matter: a line that appears only sometimes is what made the row jump when the parser
             was identified, and a screen with no line at all is what made a read that takes a minute
             behind a one-tab-per-site queue look like nothing had happened. -->
        <p class="status" :class="statusTone">{{ statusText }}</p>

        <ImportRunPanel :novel-id="id" kind="Translation" />

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
    import type { ParsedChapterLink } from "@/types/models/domain";

    import { computed, h, ref, resolveComponent, watch } from "vue";
    import ImportRunPanel from "@/components/imports/ImportRunPanel.vue";
    import { useActivity, useStartImport } from "@/composables/useActivity";
    import { useChapters } from "@/composables/useChapters";
    import { useCancelListing, useListing, useReadListing } from "@/composables/useListing";
    import { useNovel } from "@/composables/useNovels";
    import { useActivityStore } from "@/stores/activity.store";
    import { formatCount, formatWhen, siteLabel } from "@/utils/format";
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

    // The contents this book has read from a translation site, as the server kept them. The address
    // is seeded from that listing rather than held separately, so the field and the table can never
    // disagree about which site is on screen — which is exactly how a refresh used to leave a filled
    // address above an empty table.
    const { data: listingData } = useListing(id, "Translation");
    const { mutateAsync: readListing, isPending: readStarting } = useReadListing(id, "Translation");
    const { mutateAsync: stopReading, isPending: cancellingRead } = useCancelListing(id, "Translation");

    const listing = computed(() => listingData.value ?? null);
    const links = computed(() => listing.value?.entries ?? []);
    const reading = computed(() => listing.value?.state === "Reading");
    const hasEntries = computed(() => links.value.length > 0);

    const url = ref("");
    const rowSelection = ref<Record<string, boolean>>({});
    const rangeSpec = ref("");
    const createMissing = ref(false);

    // Chapters are numbered from one everywhere the reader looks, so this field takes that number and
    // the 0-based index the API stores is derived at the call, not carried around the screen.
    const startAtNumber = ref(1);

    const { mutateAsync: startImport, isPending: starting } = useStartImport(id);

    // Only what this screen decides with. Running the job — pausing, cancelling, dismissing it, and
    // reporting it chapter by chapter — belongs to ImportRunPanel, which both import screens share.
    const job = computed(() => activity.importFor("Translation"));

    const jobActive = computed(() => (
        job.value !== null
        && (job.value.state === "Queued" || job.value.state === "Running" || job.value.state === "Paused")
    ));

    // The address cannot be edited while the site is being read or while the chapters are being
    // imported. Locking the button alone left a field that accepted a new address the run behind it
    // was never going to use.
    const addressLocked = computed(() => reading.value || jobActive.value);

    const canRead = computed(() => url.value.trim() !== "" && !addressLocked.value && !readStarting.value);

    // Pressable while reading too, because that is when it stops the read.
    const canPressRead = computed(() => (reading.value ? !cancellingRead.value : canRead.value));

    const readLabel = computed(() => {
        if (reading.value) {
            return "Stop reading";
        }

        return hasEntries.value ? "Read again" : "Read the contents";
    });

    // One sentence for whichever state this screen is in. Every branch returns something, so the line
    // is never empty and the rows below it never move.
    const statusText = computed(() => {
        if (reading.value) {
            return `Reading the contents of ${siteLabel(listing.value?.url ?? url.value)}. This can take a minute, `
                + "and it keeps going if you leave this page.";
        }

        // The entries below survive a failed re-read of the same address, so the line has to account
        // for them: a list on screen under a bare error message reads as though the error produced it.
        if (listing.value?.state === "Failed" && listing.value.error !== null) {
            const kept = hasEntries.value ? " The list below is from the last read that worked." : "";

            return `${describe(listing.value.error)}${kept}`;
        }

        if (listing.value?.state === "Ready") {
            const when = listing.value.readAt === null ? "" : `, read ${formatWhen(listing.value.readAt)}`;

            return `${formatCount(links.value.length)} entries from ${siteLabel(listing.value.url)}${when}.`;
        }

        return "Paste the address of the contents page of a translation this book already has.";
    });

    const statusTone = computed(() => {
        if (reading.value) {
            return "working";
        }

        return listing.value?.state === "Failed" ? "failed" : "";
    });

    // The address the listing was read for is the address the field should show on arrival - it is
    // the same fact, and holding it twice is what let the two disagree. Only seeded while the user
    // has not typed anything, so a half-written address is never overwritten by an event landing.
    watch(listing, (loaded) => {
        if (loaded !== null && url.value.trim() === "") {
            url.value = loaded.url;
        }
    }, { immediate: true });

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

            // Wide enough for the longest label it ever carries, so swapping between reading and
            // stopping does not resize the button and shove the field beside it.
            .read {
                flex: none;
                min-width: 11rem;
                justify-content: center;
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
