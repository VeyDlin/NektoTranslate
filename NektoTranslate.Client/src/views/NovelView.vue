<template>
    <div class="novel">
        <header class="bar">
            <UButton
                :to="{ name: 'library' }"
                icon="i-material-symbols:arrow-back-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                aria-label="Back to the library"
            />

            <span class="title" :lang="scriptLangIf(novel?.title ?? '', scriptLang)">{{ novel?.title ?? "…" }}</span>

            <span v-if="novel" class="pair">{{ novel.sourceLanguage }} to {{ novel.targetLanguage }}</span>

            <span class="spacer" />

            <span v-if="rows.length > 0" class="counts">
                {{ formatCount(progress.translated) }} of {{ formatCount(progress.total) }} translated
            </span>

            <UButton
                :to="{ name: 'alignment', params: { novelId: id } }"
                icon="i-material-symbols:swap-vert-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                :disabled="alignmentDisabledReason !== null"
                :title="alignmentDisabledReason ?? undefined"
            >
                Alignment
            </UButton>

            <UButton
                :to="{ name: 'novel-settings', params: { novelId: id } }"
                icon="i-material-symbols:settings-outline-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                aria-label="Novel settings"
            />

            <UColorModeButton size="sm" />
        </header>

        <div class="actions">
            <UTabs v-model="tab" :items="tabs" variant="link" class="tabs" />

            <div class="buttons">
                <UButton
                    v-if="resumePosition"
                    size="sm"
                    color="neutral"
                    variant="subtle"
                    icon="i-material-symbols:book-ribbon-outline-rounded"
                    :to="{ name: 'reader', params: { novelId: id, chapterId: resumePosition.id } }"
                >
                    Continue chapter {{ chapterNumber(resumePosition.index) }}
                </UButton>

                <!-- Named for what they bring in, not for where it comes from: both of the first two
                     read a site, and the difference that matters is original against translation.
                     Each is also the way back to its own screen, and says so while a job is running. -->
                <ImportActionButton
                    :to="{ name: 'import-from-url', params: { novelId: id } }"
                    icon="i-material-symbols:link-rounded"
                    label="Import original"
                    :job="originalsImport"
                />

                <ImportActionButton
                    :to="{ name: 'import-translation', params: { novelId: id } }"
                    icon="i-material-symbols:translate-rounded"
                    label="Import translation"
                    :job="translationImport"
                />

                <UButton
                    size="sm"
                    color="neutral"
                    variant="ghost"
                    icon="i-material-symbols:content-paste-rounded"
                    :to="{ name: 'import-chapter', params: { novelId: id } }"
                >
                    Paste a chapter
                </UButton>

                <UButton
                    size="sm"
                    icon="i-material-symbols:play-arrow-rounded"
                    :disabled="translateDisabledReason !== null"
                    :title="translateDisabledReason ?? undefined"
                    @click="openRun('Translate')"
                >
                    Translate
                </UButton>
            </div>
        </div>

        <Transition name="pick">
            <div v-if="selectedIds.length > 0" class="picked">
                <span>{{ formatCount(selectedIds.length) }} selected</span>

                <UButton size="xs" color="neutral" variant="ghost" @click="rowSelection = {}">
                    Clear
                </UButton>

                <UDropdownMenu :items="versionMenuItems">
                    <UButton
                        size="xs"
                        color="neutral"
                        variant="ghost"
                        trailing-icon="i-material-symbols:keyboard-arrow-down-rounded"
                    >
                        Version
                    </UButton>
                </UDropdownMenu>

                <span class="spacer" />

                <UButton
                    size="xs"
                    color="error"
                    variant="ghost"
                    icon="i-material-symbols:delete-outline-rounded"
                    @click="removing = true"
                >
                    Delete
                </UButton>
            </div>
        </Transition>

        <div v-if="chaptersLoading" class="loading">
            <USkeleton v-for="index in 8" :key="index" class="skeleton" />
        </div>

        <!-- A book created with an address already told us where its chapters are. Sending that user
             to the paste form is asking them for something they have given once already. -->
        <div v-else-if="rows.length === 0 && tab === 'chapters'" class="blank">
            <h1>No chapters yet</h1>

            <p v-if="sourceHost !== null">
                This book was added with a link to {{ sourceHost }}. Read its contents and pick what
                to bring in — nothing is downloaded until you choose. Everything the agent translates
                becomes readable as soon as it lands, so you do not have to wait for the book to finish.
            </p>

            <p v-else>
                Paste the first chapter and the agent can start. Everything it translates becomes
                readable as soon as it lands, so you do not have to wait for the book to finish.
            </p>

            <div class="blank-actions">
                <UButton
                    v-if="sourceHost !== null"
                    icon="i-material-symbols:link-rounded"
                    :to="{ name: 'import-from-url', params: { novelId: id } }"
                >
                    Import from {{ sourceHost }}
                </UButton>

                <UButton
                    icon="i-material-symbols:content-paste-rounded"
                    :color="sourceHost === null ? 'primary' : 'neutral'"
                    :variant="sourceHost === null ? 'solid' : 'ghost'"
                    :to="{ name: 'import-chapter', params: { novelId: id } }"
                >
                    Paste a chapter
                </UButton>
            </div>
        </div>

        <template v-else-if="tab === 'chapters'">
            <div class="finder">
                <UInput
                    v-model="chapterQuery"
                    icon="i-material-symbols:search-rounded"
                    placeholder="Chapter number, or words from a title"
                    size="sm"
                    class="search"
                >
                    <template v-if="chapterQuery !== ''" #trailing>
                        <UButton
                            color="neutral"
                            variant="link"
                            size="sm"
                            icon="i-material-symbols:close-rounded"
                            aria-label="Clear the search"
                            @click="chapterQuery = ''"
                        />
                    </template>
                </UInput>

                <UButton
                    size="sm"
                    color="neutral"
                    variant="ghost"
                    :icon="newestFirst
                        ? 'i-material-symbols:arrow-upward-rounded'
                        : 'i-material-symbols:arrow-downward-rounded'"
                    @click="newestFirst = !newestFirst"
                >
                    {{ newestFirst ? "Newest first" : "First to last" }}
                </UButton>

                <span v-if="chapterQuery.trim() !== ''" class="found">
                    {{ formatCount(visibleRows.length) }} of {{ formatCount(rows.length) }}
                </span>

                <span v-else class="found">
                    {{ formatCount(rows.length) }} {{ rows.length === 1 ? "chapter" : "chapters" }}
                </span>
            </div>

            <ChapterTable
                v-model:selection="rowSelection"
                :rows="visibleRows"
                :novel-id="id"
                :script-lang="scriptLang"
                :show-gaps="showGaps"
            />
        </template>

        <GlossaryList
            v-else-if="tab === 'glossary'"
            :entries="glossary"
            :novel-id="id"
            :language="novel?.targetLanguage ?? ''"
            :script-lang="scriptLang"
        />

        <VoicePanel
            v-else-if="tab === 'voice'"
            :novel-id="id"
            :language="novel?.targetLanguage ?? ''"
            :rows="rows"
            @learn="openRun('LearnVoice')"
        />

        <ChatPanel v-else-if="tab === 'chat'" :novel-id="id" />

        <JobHistory v-else :jobs="jobs" />

        <StartRunModal
            v-model:open="starting"
            v-model:mode="startMode"
            :novel-id="id"
            :language="novel?.targetLanguage ?? ''"
            :rows="rows"
            :selected-ids="selectedIds"
            :average-cost="averageCost"
        />

        <DeleteChaptersModal
            v-model:open="removing"
            :novel-id="id"
            :chapters="selectedRows"
            @deleted="rowSelection = {}"
        />
    </div>
</template>

<script setup lang="ts">
    import type { DropdownMenuItem } from "@nuxt/ui";
    import type { TranslationJobMode } from "@/types/models/domain";

    import { computed, ref, watch } from "vue";
    import { useRoute, useRouter } from "vue-router";

    import ChapterTable from "@/components/chapters/ChapterTable.vue";
    import DeleteChaptersModal from "@/components/chapters/DeleteChaptersModal.vue";
    import ChatPanel from "@/components/chat/ChatPanel.vue";
    import GlossaryList from "@/components/glossary/GlossaryList.vue";
    import ImportActionButton from "@/components/imports/ImportActionButton.vue";
    import JobHistory from "@/components/jobs/JobHistory.vue";
    import StartRunModal from "@/components/jobs/StartRunModal.vue";
    import VoicePanel from "@/components/voice/VoicePanel.vue";
    import { useActivity } from "@/composables/useActivity";
    import { useChapters, useMakeVersionsCurrent } from "@/composables/useChapters";
    import { useGlossary } from "@/composables/useGlossary";
    import { useJobs } from "@/composables/useJobs";
    import { useNovelProgress } from "@/composables/useNovelProgress";
    import { useNovel } from "@/composables/useNovels";
    import { useActivityStore } from "@/stores/activity.store";
    import { useProgressStore } from "@/stores/progress.store";
    import { chapterNumber, formatCount, hostOf } from "@/utils/format";
    import { scriptLangFor, scriptLangIf } from "@/utils/language";
    import { notifySuccess } from "@/utils/notify";
    import { bulkCurrentToastTitle } from "@/utils/translationVersions";


    const props = defineProps<{ novelId: string }>();

    const route = useRoute();
    const router = useRouter();

    const id = computed(() => Number(props.novelId));

    // The open tab lives in the address as its hash - /novels/10#voice - so a reload, the back
    // button and a pasted link all land on the tab that was meant, not on the chapter list every
    // time. The chapter list is the default and carries no hash, so a plain book address stays
    // plain. `replace` rather than `push`: switching tabs is not a step the back button should
    // have to retrace one by one.
    const TabNames = ["chapters", "glossary", "voice", "chat", "runs"] as const;

    function tabFromHash(hash: string): string {
        const name = hash.replace(/^#/, "");

        return (TabNames as readonly string[]).includes(name) ? name : "chapters";
    }

    const tab = ref(tabFromHash(route.hash));

    watch(tab, (value) => {
        const hash = value === "chapters" ? "" : `#${value}`;

        if (route.hash !== hash) {
            void router.replace({ hash });
        }
    });

    watch(() => route.hash, (hash) => {
        tab.value = tabFromHash(hash);
    });
    const starting = ref(false);
    const removing = ref(false);
    const startMode = ref<TranslationJobMode>("Translate");
    const chapterQuery = ref("");
    const newestFirst = ref(false);

    const progressStore = useProgressStore();
    const activityStore = useActivityStore();

    // The table owns selection in TanStack's shape: a map of row id to boolean. Chapter ids are the
    // row ids, so the picked set falls out of the keys without a parallel structure to keep in sync.
    const rowSelection = ref<Record<string, boolean>>({});

    const selectedIds = computed(() => Object.entries(rowSelection.value)
        .filter(([, picked]) => picked)
        .map(([id]) => Number(id)));

    // Feeds run.store and activity.store from whatever is already live, so the strip below shows an
    // import or a translation run in progress the moment this screen is the one landed on, rather
    // than only after a visit to the run's own sub-screen.
    useActivity(id);

    const { data: novelData } = useNovel(id);
    const { data: chapterData, isLoading: chaptersLoading } = useChapters(id);
    const { data: glossaryData } = useGlossary(id);
    const { data: jobData } = useJobs(id);
    const { progress } = useNovelProgress(id);

    const novel = computed(() => novelData.value ?? null);
    const rows = computed(() => chapterData.value ?? []);
    const glossary = computed(() => glossaryData.value ?? []);
    const jobs = computed(() => jobData.value ?? []);

    const scriptLang = computed(() => (novel.value === null ? undefined : scriptLangFor(novel.value.sourceLanguage)));

    const sourceHost = computed(() => hostOf(novel.value?.sourceUrl ?? null));

    // The delete confirmation names the chapters rather than counting them, so the rows themselves
    // have to reach it — an id list would only let it say "3 chapters", which is not enough to hand
    // someone before an irreversible action.
    const selectedRows = computed(() => rows.value.filter(row => rowSelection.value[String(row.id)] === true));

    const { mutateAsync: makeVersionsCurrent, isPending: isMakingVersionsCurrent } = useMakeVersionsCurrent(id);

    const versionMenuItems = computed<DropdownMenuItem[]>(() => [
        {
            label: "Use first version",
            disabled: isMakingVersionsCurrent.value,
            onSelect: () => {
                void applyVersionPick("First");
            },
        },
        {
            label: "Use newest version",
            disabled: isMakingVersionsCurrent.value,
            onSelect: () => {
                void applyVersionPick("Newest");
            },
        },
    ]);

    async function applyVersionPick(pick: "First" | "Newest"): Promise<void> {
        const { changed, skipped } = await makeVersionsCurrent({ chapterIds: [...selectedIds.value], pick });

        notifySuccess(bulkCurrentToastTitle(pick, changed, skipped));
    }

    // The two site-reading screens each own one kind of job, and each of their buttons carries that
    // job's progress, so the way back to a running import is the button that started it.
    const originalsImport = computed(() => activityStore.importFor("Originals", id.value));
    const translationImport = computed(() => activityStore.importFor("Translation", id.value));

    // Both buttons dim the moment the book has nothing in it — correctly, since there is nothing to
    // align or translate yet — but a greyed-out control with no explanation reads as broken rather
    // than as "come back once you've imported something". The reason is what makes the disabled state
    // legible instead of just present.
    const translateDisabledReason = computed(() => (
        rows.value.length === 0 ? "No chapters yet — nothing to translate" : null
    ));

    const alignmentDisabledReason = computed(() => (
        rows.value.length === 0 ? "No chapters yet — nothing to align" : null
    ));

    // A digits-only query matches the index by prefix, so typing towards a number narrows the list
    // the way scrolling towards it would: "14" reaches chapter 14, then 140-149, then 1400-1499.
    // Anything else is a title search. Two thousand chapters is a long way to scroll for one of them.
    const visibleRows = computed(() => {
        const needle = chapterQuery.value.trim();
        const numeric = /^\d+$/.test(needle);

        const matched = needle === ""
            ? rows.value
            : rows.value.filter(row => (numeric
                ? String(chapterNumber(row.index)).startsWith(needle)
                : row.title.toLowerCase().includes(needle.toLowerCase())));

        return newestFirst.value ? [...matched].reverse() : matched;
    });

    // A gap only means something when the rows in front of the table are the whole book. A search
    // narrows which chapters are shown, and every chapter it filtered out would read as a false gap.
    // Newest-first only reverses the order, and the table reads the order for itself.
    const showGaps = computed(() => chapterQuery.value.trim() === "");

    // A translation in progress grows from the end, so the chapter worth returning to is rarely the
    // one at the top of the list.
    // Resolved against the chapter list every time rather than trusted as stored. The saved position
    // is a snapshot taken when the chapter was open, and a snapshot outlives what it describes: after
    // chapters are deleted it would otherwise keep offering a number that no longer belongs to
    // anything. The id decides whether to show the button at all, and the row it points at - not the
    // snapshot - decides which number the button says.
    const resumePosition = computed(() => {
        const saved = progressStore.positionFor(id.value);

        if (saved === null) {
            return null;
        }

        return rows.value.find(row => row.id === saved.chapterId) ?? null;
    });

    const tabs = computed(() => [
        { label: "Chapters", value: "chapters" },
        { label: `Glossary (${formatCount(glossary.value.length)})`, value: "glossary" },
        { label: "Voice", value: "voice" },
        { label: "Chat", value: "chat" },
        { label: "Runs", value: "runs" },
    ]);

    // Derived from what past translate runs actually cost rather than a fixed rate. The price of a
    // chapter depends on its length and the model, both of which vary per book, so a number taken
    // from this book's own history is the only estimate worth showing — and only its own runs count,
    // since a voice-learning pass is one call over a whole range rather than a per-chapter cost and
    // would skew the average toward nothing a translation run actually resembles.
    const averageCost = computed(() => {
        const finished = jobs.value.filter(job => job.mode === "Translate" && job.processedCount > 0);

        if (finished.length === 0) {
            return null;
        }

        const spent = finished.reduce((total, job) => total + job.costUsd, 0);
        const chapters = finished.reduce((total, job) => total + job.processedCount, 0);

        return spent / chapters;
    });


    // Both the header's own Translate button and the Voice tab's call to action open the same
    // dialog; which run it starts is just which mode it opens already set to.
    function openRun(mode: TranslationJobMode): void {
        startMode.value = mode;
        starting.value = true;
    }
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .novel {
        position: relative;
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

            .title {
                font-size: var(--nt-text-md);
                color: var(--ui-text-highlighted);
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }

            .pair,
            .counts {
                flex: none;
                color: var(--ui-text-muted);
            }

            .spacer {
                flex: 1;
            }
        }

        .actions {
            flex: none;
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 1rem;
            padding: 0 1rem 0 1.5rem;
            border-bottom: 1px solid var(--ui-border);

            .buttons {
                display: flex;
                gap: 0.25rem;
            }
        }

        // Floats over the list instead of being a row inserted above it. A bar that takes its own
        // line pushes the whole table down the moment a checkbox is ticked, which moves the row the
        // user was aiming at out from under the cursor - the one place in the interface where a
        // layout shift is guaranteed to be mid-click.
        .picked {
            position: absolute;
            left: 50%;
            bottom: 1.25rem;
            transform: translateX(-50%);
            z-index: 20;
            display: flex;
            align-items: center;
            gap: 0.75rem;
            padding: 0.5rem 0.75rem 0.5rem 1rem;
            border: 1px solid var(--ui-border);
            border-radius: var(--ui-radius);
            background: var(--ui-bg-elevated);
            box-shadow: 0 8px 24px rgb(0 0 0 / 25%);

            .spacer {
                width: 0.5rem;
            }
        }

        .finder {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.75rem;
            padding: 0.625rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);

            // Takes whatever the row has left. A fixed 20rem field with a thousand pixels of empty
            // bar beside it looks like the layout gave up halfway.
            .search {
                flex: 1;
                min-width: 0;
            }

            .found {
                color: var(--ui-text-muted);
            }
        }

        .loading {
            padding: 1rem 1.5rem;

            .skeleton {
                height: 1.5rem;
                margin-bottom: 0.75rem;
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
                margin: 0 0 1.5rem;
                line-height: 1.6;
                color: var(--ui-text-muted);
            }

            .blank-actions {
                display: flex;
                gap: 0.5rem;
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
