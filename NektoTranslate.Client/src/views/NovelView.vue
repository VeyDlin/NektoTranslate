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
                aria-label="Chapter alignment"
            />

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
                    :to="{ name: 'reader', params: { novelId: id, chapterId: resumePosition.chapterId } }"
                >
                    Continue chapter {{ chapterNumber(resumePosition.index) }}
                </UButton>

                <UButton
                    size="sm"
                    color="neutral"
                    variant="ghost"
                    icon="i-material-symbols:link-rounded"
                    :to="{ name: 'import-from-url', params: { novelId: id } }"
                >
                    From a site
                </UButton>

                <UButton
                    size="sm"
                    color="neutral"
                    variant="ghost"
                    icon="i-material-symbols:translate-rounded"
                    :to="{ name: 'import-translation', params: { novelId: id } }"
                >
                    Existing translation
                </UButton>

                <UButton
                    size="sm"
                    color="neutral"
                    variant="ghost"
                    icon="i-material-symbols:content-paste-rounded"
                    :to="{ name: 'import-chapter', params: { novelId: id } }"
                >
                    Paste
                </UButton>

                <UButton
                    size="sm"
                    icon="i-material-symbols:play-arrow-rounded"
                    :disabled="rows.length === 0"
                    @click="starting = true"
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
            </div>
        </Transition>

        <div v-if="chaptersLoading" class="loading">
            <USkeleton v-for="index in 8" :key="index" class="skeleton" />
        </div>

        <div v-else-if="rows.length === 0 && tab === 'chapters'" class="blank">
            <h1>No chapters yet</h1>
            <p>
                Paste the first chapter and the agent can start. Everything it translates becomes
                readable as soon as it lands, so you do not have to wait for the book to finish.
            </p>
            <UButton
                icon="i-material-symbols:content-paste-rounded"
                :to="{ name: 'import-chapter', params: { novelId: id } }"
            >
                Add chapter
            </UButton>
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
                    {{ formatCount(rows.length) }} chapters
                </span>
            </div>

            <ChapterTable
                v-model:selection="rowSelection"
                :rows="visibleRows"
                :novel-id="id"
                :script-lang="scriptLang"
            />
        </template>

        <GlossaryList
            v-else-if="tab === 'glossary'"
            :entries="glossary"
            :novel-id="id"
            :language="novel?.targetLanguage ?? ''"
            :script-lang="scriptLang"
        />

        <ChatPanel v-else-if="tab === 'chat'" :novel-id="id" />

        <JobHistory v-else :jobs="jobs" />

        <StartRunModal
            v-model:open="starting"
            :novel-id="id"
            :rows="rows"
            :selected-ids="selectedIds"
            :average-cost="averageCost"
        />
    </div>
</template>

<script setup lang="ts">
    import { computed, ref } from "vue";

    import ChapterTable from "@/components/chapters/ChapterTable.vue";
    import ChatPanel from "@/components/chat/ChatPanel.vue";
    import GlossaryList from "@/components/glossary/GlossaryList.vue";
    import JobHistory from "@/components/jobs/JobHistory.vue";
    import StartRunModal from "@/components/jobs/StartRunModal.vue";
    import { useChapters } from "@/composables/useChapters";
    import { useGlossary } from "@/composables/useGlossary";
    import { useJobs } from "@/composables/useJobs";
    import { useNovelProgress } from "@/composables/useNovelProgress";
    import { useNovel } from "@/composables/useNovels";
    import { useProgressStore } from "@/stores/progress.store";
    import { chapterNumber, formatCount } from "@/utils/format";
    import { scriptLangFor, scriptLangIf } from "@/utils/language";


    const props = defineProps<{ novelId: string }>();

    const id = computed(() => Number(props.novelId));

    const tab = ref("chapters");
    const starting = ref(false);
    const chapterQuery = ref("");
    const newestFirst = ref(false);

    const progressStore = useProgressStore();

    // The table owns selection in TanStack's shape: a map of row id to boolean. Chapter ids are the
    // row ids, so the picked set falls out of the keys without a parallel structure to keep in sync.
    const rowSelection = ref<Record<string, boolean>>({});

    const selectedIds = computed(() => Object.entries(rowSelection.value)
        .filter(([, picked]) => picked)
        .map(([id]) => Number(id)));

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

    // A translation in progress grows from the end, so the chapter worth returning to is rarely the
    // one at the top of the list.
    const resumePosition = computed(() => {
        const saved = progressStore.positionFor(id.value);

        if (saved === null) {
            return null;
        }

        return rows.value.some(row => row.id === saved.chapterId) ? saved : null;
    });

    const tabs = computed(() => [
        { label: "Chapters", value: "chapters" },
        { label: `Glossary (${formatCount(glossary.value.length)})`, value: "glossary" },
        { label: "Chat", value: "chat" },
        { label: "Runs", value: "runs" },
    ]);

    // Derived from what past runs actually cost rather than a fixed rate. The price of a chapter
    // depends on its length and the model, both of which vary per book, so a number taken from this
    // book's own history is the only estimate worth showing.
    const averageCost = computed(() => {
        const finished = jobs.value.filter(job => job.processedCount > 0);

        if (finished.length === 0) {
            return null;
        }

        const spent = finished.reduce((total, job) => total + job.costUsd, 0);
        const chapters = finished.reduce((total, job) => total + job.processedCount, 0);

        return spent / chapters;
    });
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .novel {
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

        .picked {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.75rem;
            padding: 0.5rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);
            background: var(--ui-bg-elevated);
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
