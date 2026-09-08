<template>
    <div class="import-translation">
        <AppBar>
            <UButton
                :to="{ name: 'novel', params: { novelId } }"
                icon="i-material-symbols:arrow-back-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                aria-label="Back to the chapter list"
            />

            <span class="book" data-bar-text>{{ novel?.title ?? "" }}</span>

            <span class="where" data-bar-text>Import an existing translation</span>

            <span class="spacer" data-bar-text />

            <UButton :to="{ name: 'alignment', params: { novelId } }" size="sm" color="neutral" variant="ghost">
                Alignment
            </UButton>

            <UColorModeButton size="sm" />
        </AppBar>

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

        <!-- The report above and the picker below used to be one fixed stack fighting over a single
             screen: enough room to read a long per-chapter report crowded the table out, and enough
             room for the table buried the report behind its own scrollbar. The split lets the reader
             drag the boundary to whichever side needs it right now. -->
        <ResizableSplit :top-visible="job !== null" storage-key="import-split:translation">
            <template #top>
                <ImportRunPanel :novel-id="id" kind="Translation" />
            </template>

            <template #bottom>
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

                        <!-- Always here, whether or not the last read found anything new - a button that
                             shows up only sometimes is a button whose absence has to be read as an answer. -->
                        <UButton
                            size="xs"
                            color="neutral"
                            variant="ghost"
                            :disabled="jobActive || newEntryCount === 0"
                            @click="selectNew"
                        >
                            Select new
                        </UButton>

                        <UButton size="xs" color="neutral" variant="ghost" :disabled="jobActive" @click="rowSelection = {}">
                            Clear
                        </UButton>

                        <USwitch v-model="replaceExisting" :disabled="jobActive" label="Replace existing" />

                        <span class="gap" />

                        <span class="starts">First entry becomes chapter</span>

                        <UInputNumber
                            v-model="startAtNumber"
                            :min="1"
                            :max="100000"
                            :disabled="jobActive"
                            size="sm"
                            class="start"
                            @update:model-value="touchStartAt"
                        />

                        <span class="spacer" />

                        <UButton
                            size="sm"
                            class="import"
                            :disabled="selectedLinks.length === 0 || jobActive"
                            :loading="starting"
                            @click="runImport"
                        >
                            {{ importLabel }}
                        </UButton>
                    </div>
                </Transition>

                <!-- The one thing this screen exists to get right. Translation sites routinely open with a
                     translator's note, so entry one is not chapter one, and every later chapter inherits the
                     mistake. Showing the mapping before the import makes that visible while it is still
                     free to fix. -->
                <!-- Always on screen once there is a list, at one fixed height: it changes what it says,
                     never whether it is there, so ticking an entry does not push the list down under
                     the pointer that just ticked it. -->
                <p v-if="links.length > 0" class="mapping">
                    <template v-if="selectedLinks.length > 0">
                        <strong>{{ firstTitle }}</strong> → chapter {{ startAtNumber }}
                        <template v-if="selectedLinks.length > 1">
                            &nbsp;·&nbsp;
                            <strong>{{ lastTitle }}</strong> → chapter {{ startAtNumber + selectedLinks.length - 1 }}
                        </template>
                        <template v-if="alreadyImportedCount > 0">
                            &nbsp;·&nbsp;{{ formatCount(alreadyImportedCount) }} already in the book will be
                            {{ replaceExisting ? "replaced" : "skipped" }}
                        </template>
                    </template>
                    <template v-else>
                        Nothing chosen yet. Once entries are chosen, this line says which chapter each end
                        of the choice lands on.
                    </template>
                </p>

                <!-- A book with no chapters at all is somebody else's translation and nothing more, and
                     there is nothing in it an offset could be wrong against - so its chapters are made
                     without asking, and the switch shows that as on and not the user's to turn off. For a
                     book that has some chapters, an entry landing outside them is as likely a wrong offset
                     as a missing original, and only the user knows which. The row is always here, like
                     the mapping above it, for the same reason. -->
                <div v-if="links.length > 0" class="orphans">
                    <USwitch
                        :model-value="willCreateMissing"
                        :disabled="bookIsEmpty || jobActive"
                        aria-label="Create chapters with no original for entries that land where the book has none"
                        @update:model-value="(value: boolean) => createMissing = value"
                    />

                    <span class="text">
                        <template v-if="bookIsEmpty">
                            The book has no chapters yet, so every chosen entry will be created as a chapter
                            with no original — readable and editable, but nothing to translate from.
                        </template>
                        <template v-else-if="selectedLinks.length === 0">
                            An entry that lands on a chapter the book does not have is reported and skipped —
                            or, with this on, created as a chapter with no original.
                        </template>
                        <template v-else-if="missingCount === 0">
                            Every chosen entry lands on a chapter the book has.
                        </template>
                        <template v-else>
                            <strong>{{ formatCount(missingCount) }}</strong>
                            {{ missingCount === 1 ? "entry lands on a chapter" : "entries land on chapters" }}
                            the book does not have.
                            <template v-if="createMissing">
                                {{ missingCount === 1 ? "It" : "They" }} will be created with no original —
                                readable and editable, but nothing to translate from.
                            </template>
                            <template v-else>
                                {{ missingCount === 1 ? "It" : "They" }} will be reported and skipped. Turn
                                this on if the original does not exist anywhere.
                            </template>
                        </template>
                    </span>
                </div>

                <UTable
                    v-if="links.length > 0"
                    v-model:row-selection="rowSelection"
                    :row-selection-options="rowSelectionOptions"
                    :data="links"
                    :columns="columns"
                    :get-row-id="getRowId"
                    :virtualize="{ estimateSize: 40, overscan: 14 }"
                    sticky
                    class="flex-1 min-h-0"
                    :ui="{ th: 'py-2 text-xs font-normal text-dimmed', td: 'py-0 h-10' }"
                >
                    <!-- Fixed width so a row moving from nothing to "Chapter 12" never shifts the column
                         beside it, and every value here names the row's state on screen rather than leaving a
                         disabled checkbox to explain itself. -->
                    <template #relationship-cell="{ row }">
                        <span v-if="row.original.state === 'Imported'" class="relationship">
                            {{ importedLabel(row.original) }}
                        </span>

                        <span
                            v-else-if="row.original.state === 'Failed'"
                            class="relationship failed"
                            :title="row.original.error === null ? undefined : describe(row.original.error)"
                        >
                            Failed
                        </span>

                        <UBadge v-else-if="row.original.isNew" label="New" color="info" variant="subtle" size="sm" />
                    </template>
                </UTable>

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
            </template>
        </ResizableSplit>
    </div>
</template>

<script setup lang="ts">
    import type { TableColumn } from "@nuxt/ui";
    import type { ListingEntry } from "@/types/models/domain";

    import { computed, h, ref, resolveComponent, watch } from "vue";
    import AppBar from "@/components/common/AppBar.vue";
    import ResizableSplit from "@/components/common/ResizableSplit.vue";
    import ImportRunPanel from "@/components/imports/ImportRunPanel.vue";
    import { useActivity, useStartImport } from "@/composables/useActivity";
    import { useChapters } from "@/composables/useChapters";
    import { useCancelListing, useListing, useReadListing } from "@/composables/useListing";
    import { useNovel } from "@/composables/useNovels";
    import { useActivityStore } from "@/stores/activity.store";
    import { useImportWorkbenchStore } from "@/stores/importWorkbench.store";
    import { chapterNumber, formatCount, formatWhen, siteLabel } from "@/utils/format";
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
    const rangeSpec = ref("");

    const importWorkbench = useImportWorkbenchStore();

    // The bench entry has to exist before anything below reads it. Ensured here, once, and again if
    // this component is ever kept alive across a change of novel - never inside a computed, so
    // creating the default entry is a plain effect of arriving on the screen rather than a side
    // effect of reading from it.
    watch(id, (novelId) => {
        importWorkbench.bench(novelId, "Translation");
    }, { immediate: true });

    const bench = computed(() => importWorkbench.bench(id.value, "Translation"));

    // Everything below is a view onto the store entry rather than state of its own, which is what
    // lets the pick, the start chapter and both switches all survive leaving this screen and coming
    // back to it - reloaded or not.
    const rowSelection = computed<Record<string, boolean>>({
        get: () => Object.fromEntries(bench.value.selectedUrls.map(sourceUrl => [sourceUrl, true])),
        set: (value: Record<string, boolean>) => {
            importWorkbench.patch(id.value, "Translation", {
                selectedUrls: Object.keys(value).filter(sourceUrl => value[sourceUrl] === true),
            });
        },
    });

    // Chapters are numbered from one everywhere the reader looks, so this field takes that number and
    // the 0-based index the API stores is derived at the call, not carried around the screen. Falls
    // back to one for display only - the store itself keeps null until a pick or a keystroke actually
    // sets it, so an untouched bench never persists a number nobody chose.
    const startAtNumber = computed<number>({
        get: () => bench.value.startAtNumber ?? 1,
        set: (value: number) => importWorkbench.patch(id.value, "Translation", { startAtNumber: value }),
    });

    const createMissing = computed<boolean>({
        get: () => bench.value.createMissing,
        set: (value: boolean) => importWorkbench.patch(id.value, "Translation", { createMissing: value }),
    });

    const replaceExisting = computed<boolean>({
        get: () => bench.value.replaceExisting,
        set: (value: boolean) => importWorkbench.patch(id.value, "Translation", { replaceExisting: value }),
    });

    // Order matters and is the site's, not the click order: entries are mapped to consecutive
    // chapters in the order they appear in the contents.
    const selectedLinks = computed(() => links.value.filter(link => rowSelection.value[link.sourceUrl] === true));

    const firstTitle = computed(() => selectedLinks.value[0]?.title ?? "");
    const lastTitle = computed(() => selectedLinks.value[selectedLinks.value.length - 1]?.title ?? "");

    // Feeds the "Select new" button's disabled state and the status line above: a read that turned up
    // nothing new says so only while nothing is picked yet, since once something is picked the line
    // belongs to the pick, not the read.
    const newEntryCount = computed(() => links.value.filter(entry => entry.isNew).length);

    // The range field can still pick a row already in the book - its checkbox is disabled, but typing
    // "1-20" does not consult it - so the preview below has to account for what the server will
    // actually do with those rows: skip them.
    const alreadyImportedCount = computed(() => selectedLinks.value.filter(entry => entry.state === "Imported").length);

    // Spelled out only once Replace would actually do something to the pick - turning the switch on
    // with nothing already in the book selected leaves the button reading exactly as it did before.
    const importLabel = computed<string>(() => {
        const count = formatCount(selectedLinks.value.length);
        const suffix = (replaceExisting.value && alreadyImportedCount.value > 0)
            ? `, replacing ${formatCount(alreadyImportedCount.value)}`
            : "";

        return `Import ${count} into ${language.value}${suffix}`;
    });

    const { mutateAsync: startImport, isPending: starting } = useStartImport(id);

    // Only what this screen decides with. Running the job — pausing, cancelling, dismissing it, and
    // reporting it chapter by chapter — belongs to ImportRunPanel, which both import screens share.
    const job = computed(() => activity.importFor("Translation", id.value));

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

            // Told here, and only while nothing is picked yet: a re-read of a long-running translation
            // buries a couple of new rows at the bottom of the table, and once something is picked
            // this line belongs to the pick rather than to the read.
            const since = (newEntryCount.value > 0 && selectedLinks.value.length === 0)
                ? `, ${formatCount(newEntryCount.value)} new since the last read`
                : "";

            return `${formatCount(links.value.length)} entries from ${siteLabel(listing.value.url)}${when}${since}.`;
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

    // The table's own header checkbox must not be able to select what a row's own checkbox refuses
    // to - a row already in the book stays out of "select all" however "all" is triggered, unless
    // Replace is on, in which case a row already in the book is exactly what it targets.
    const rowSelectionOptions = {
        enableRowSelection: (row: { original: ListingEntry }) => replaceExisting.value || row.original.state !== "Imported",
    };

    const columns: TableColumn<ListingEntry>[] = [
        {
            id: "select",
            header: ({ table }) => h(UCheckbox, {
                "modelValue": table.getIsSomeRowsSelected() ? "indeterminate" : table.getIsAllRowsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => table.toggleAllRowsSelected(!!value),
                "disabled": jobActive.value,
                "aria-label": "Select every entry",
            }),
            // A row already in the book cannot be picked - the server would only skip it - so its
            // checkbox is disabled alongside the "In the book" column that says why. Replace turns
            // that off: the row is then exactly what the import is meant to act on.
            cell: ({ row }) => h(UCheckbox, {
                "modelValue": row.getIsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => row.toggleSelected(!!value),
                "disabled": jobActive.value || (!replaceExisting.value && row.original.state === "Imported"),
                "aria-label": `Select ${row.original.title}`,
            }),
            meta: { class: { th: "w-8", td: "w-8" } },
        },
        {
            accessorKey: "title",
            header: "Entry",
        },
        {
            id: "relationship",
            header: "In the book",
            meta: { class: { th: "w-32", td: "w-32 whitespace-nowrap" } },
        },
    ];

    // Which chapters the book actually has, so the mapping can say how many of the chosen entries
    // have nowhere to land before the import runs rather than after it.
    const existingIndices = computed(() => new Set((chapterData.value ?? []).map(row => row.index)));

    const missingCount = computed(() => selectedLinks.value
        .filter((_, position) => !existingIndices.value.has(startAtNumber.value - 1 + position))
        .length);

    // Known to be empty, not merely not loaded yet: until the chapter list has arrived the screen
    // behaves as though the book had chapters, which only costs a moment of the switch being shown.
    const bookIsEmpty = computed(() => chapterData.value !== undefined && chapterData.value.length === 0);

    // What the import is actually told. The switch is the user's answer for a book with chapters;
    // an empty book has already answered.
    const willCreateMissing = computed(() => createMissing.value || bookIsEmpty.value);

    function getRowId(link: ListingEntry): string {
        return link.sourceUrl;
    }


    // `chapterIndex` is only null when the state is not Imported - the server guarantees the two
    // travel together - so the empty string here never actually reaches the screen.
    function importedLabel(entry: ListingEntry): string {
        return entry.chapterIndex === null ? "" : `Chapter ${chapterNumber(entry.chapterIndex)}`;
    }


    // Rows already in the book are never a valid pick - the server would just skip them - so "all"
    // means all of what is left to bring in, not literally every row on screen. With Replace on, a
    // row already in the book is a valid pick again, so it joins the rest.
    function selectAll(): void {
        rowSelection.value = Object.fromEntries(
            links.value
                .filter(entry => replaceExisting.value || entry.state !== "Imported")
                .map(entry => [entry.sourceUrl, true]),
        );
    }


    // Beside "Select all", for the read that follows up on an earlier one: pick only what showed up
    // since this address was last read, without hand-picking rows out of a list of hundreds.
    function selectNew(): void {
        rowSelection.value = Object.fromEntries(
            links.value.filter(entry => entry.isNew).map(entry => [entry.sourceUrl, true]),
        );
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


    // The default follows the pick instead of sitting at one: choosing entries six through ten should
    // already read "chapter six" rather than landing there only once the number is corrected by hand.
    // Only runs while the user has not typed a number of their own - see `startAtTouched` - and gives
    // up tracking once the pick is cleared, so a fresh selection starts the guess over rather than
    // keeping whatever the last one left behind.
    watch(selectedLinks, (chosen) => {
        if (chosen.length === 0) {
            importWorkbench.patch(id.value, "Translation", { startAtTouched: false });

            return;
        }

        if (!bench.value.startAtTouched) {
            startAtNumber.value = links.value.indexOf(chosen[0]) + 1;
        }
    });


    // The same event that moves `startAtNumber` also marks it touched, so the watcher above stops
    // following the pick the moment the number has been changed by hand.
    function touchStartAt(): void {
        importWorkbench.patch(id.value, "Translation", { startAtTouched: true });
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
            chapters: selectedLinks.value.map(entry => ({ sourceUrl: entry.sourceUrl, title: entry.title })),
            language: language.value,
            startAtChapterIndex: startAtNumber.value - 1,
            createMissingChapters: willCreateMissing.value,
            replaceExisting: replaceExisting.value,
        });

        importWorkbench.clearPick(id.value, "Translation");
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

        // One line, always. Labels that wrapped to two lines made the bar twice as tall on the
        // screens where they did not fit, and a control bar whose height depends on the window is
        // a control bar that shoves the table under it around.
        .picked {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.75rem;
            padding: 0.5rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);
            background: var(--ui-bg-elevated);
            white-space: nowrap;

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

            // Wide enough for "Import 999 into Japanese, replacing 999" - its longest ordinary form -
            // so turning Replace on and off never resizes the button and shifts the bar around it.
            .import {
                flex: none;
                min-width: 18rem;
                justify-content: center;
            }
        }

        // Tall enough for its longest sentence on two lines and never shorter, so the row keeps one
        // height through every state it can be in - the same rule the mapping line above follows.
        .orphans {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.75rem;
            min-height: 4rem;
            padding: 0.375rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);

            .text {
                max-width: 60rem;
                line-height: 1.5;
                color: var(--ui-text-muted);

                strong {
                    color: var(--ui-text-highlighted);
                }
            }
        }

        // One line at a fixed height, whatever it says. A long title is cut with an ellipsis rather
        // than allowed to wrap, because a second line here would move the whole list below it.
        .mapping {
            flex: none;
            height: 2.5rem;
            margin: 0;
            padding: 0 1.5rem;
            border-bottom: 1px solid var(--ui-border);
            font-size: var(--nt-text-sm);
            line-height: 2.5rem;
            color: var(--ui-text-muted);
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;

            strong {
                color: var(--ui-text-highlighted);
                font-weight: 400;
            }
        }

        .relationship {
            font-size: var(--nt-text-sm);
            color: var(--ui-text-muted);

            &.failed {
                color: var(--ui-error);
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
