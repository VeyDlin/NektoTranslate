<template>
    <div class="reader">
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

            <span v-if="chapter" class="where">Chapter {{ chapterNumber(chapter.index) }}</span>

            <span class="spacer" />

            <div class="reading-controls">
                <UButton
                    v-if="hasRuby"
                    size="sm"
                    color="neutral"
                    variant="ghost"
                    :aria-pressed="reader.showRuby"
                    @click="reader.toggleRuby()"
                >
                    {{ reader.showRuby ? "Hide furigana" : "Show furigana" }}
                </UButton>

                <!-- A chapter with no original has one thing to read, so the choice is not offered.
                     Leaving two of the three buttons to select an empty pane would be a control that
                     only ever disappoints. -->
                <UFieldGroup v-if="hasOriginal" size="sm">
                    <UButton
                        v-for="option in modeOptions"
                        :key="option.value"
                        :color="reader.mode === option.value ? 'primary' : 'neutral'"
                        :variant="reader.mode === option.value ? 'solid' : 'outline'"
                        :aria-pressed="reader.mode === option.value"
                        @click="reader.setMode(option.value)"
                    >
                        {{ option.label }}
                    </UButton>
                </UFieldGroup>

                <ReaderSettings />
            </div>

            <UColorModeButton size="sm" />
        </header>

        <div v-if="isLoading" class="loading">
            <USkeleton class="skeleton wide" />
            <USkeleton v-for="index in 6" :key="index" class="skeleton" />
        </div>

        <!-- Loaded and found nothing: the chapter was deleted, or the address is stale. Says so where
             the text would have been, at the text's height, so the footer stays put. -->
        <div v-else-if="!chapter" class="missing">
            <p>This chapter is not in the book.</p>
        </div>

        <!-- Everything in a pane shares the text's own column - the prose, the line about which
             rendering this is, the turn to the neighbouring chapters - so the reader's width and
             alignment settings move all of it together. The turn comes after the last paragraph,
             where a reader who has just finished is looking, and only in the pane being read. -->
        <div v-else class="panes" :class="paneClasses" :style="paneStyle">
            <article v-if="effectiveMode !== 'translation'" class="pane source-pane">
                <div
                    class="prose column source"
                    :class="{ 'no-ruby': !reader.showRuby }"
                    :lang="scriptLangIf(sourceHtml, scriptLang)"
                    v-html="sourceHtml"
                />

                <ChapterTurn
                    v-if="effectiveMode === 'source'"
                    class="column"
                    :novel-id="novelId"
                    :previous="previous"
                    :next="next"
                    :script-lang="scriptLang"
                />
            </article>

            <article v-if="effectiveMode !== 'source'" class="pane translation-pane">
                <!-- Which rendering is on the page, and what it cost, before the text rather than
                     under a footer: it is a fact about the text that follows. One fixed line whether
                     it holds a choice of versions or a single label. -->
                <div v-if="shown" class="meta column">
                    <USelect
                        v-if="chapter.translations.length > 1"
                        v-model="shownId"
                        :items="translationOptions"
                        size="xs"
                        variant="ghost"
                        class="versions"
                    />
                    <span v-else class="origin">{{ translationOriginLabel(shown.origin) }}</span>

                    <span v-if="shown.costUsd !== null" class="cost">{{ formatCost(shown.costUsd) }}</span>
                </div>

                <div v-if="shown" class="prose column" v-html="shownHtml" />

                <!-- Prose as it is written. Shown only while no stored translation exists, so a
                     half-finished draft can never sit beside the finished text it belongs to. -->
                <div v-else-if="streaming" class="prose column streaming">
                    <span v-html="streamingHtml" />
                    <span class="cursor" aria-hidden="true" />
                </div>

                <div v-else class="pending">
                    <p class="headline">{{ pendingHeadline }}</p>

                    <UButton
                        v-if="chapter.translationState === 'None' || chapter.translationState === 'Failed'"
                        size="sm"
                        :loading="isStarting"
                        @click="translateThisChapter"
                    >
                        Translate this chapter
                    </UButton>
                </div>

                <ChapterTurn
                    class="column"
                    :novel-id="novelId"
                    :previous="previous"
                    :next="next"
                    :script-lang="scriptLang"
                />
            </article>
        </div>
    </div>
</template>

<script setup lang="ts">
    import type { ReaderMode } from "@/stores/reader.store";
    import { onKeyStroke } from "@vueuse/core";
    import { computed, ref, watch } from "vue";

    import { useRouter } from "vue-router";
    import ChapterTurn from "@/components/reader/ChapterTurn.vue";
    import ReaderSettings from "@/components/reader/ReaderSettings.vue";
    import { useChapter, useChapters } from "@/composables/useChapters";
    import { useStartJob } from "@/composables/useJobs";
    import { useNovel } from "@/composables/useNovels";
    import { useProgressStore } from "@/stores/progress.store";
    import { useReaderStore } from "@/stores/reader.store";
    import { useRunStore } from "@/stores/run.store";
    import { chapterNumber, formatCost, formatWhen, translationOriginLabel } from "@/utils/format";
    import { scriptLangFor, scriptLangIf } from "@/utils/language";
    import { renderMarkdown } from "@/utils/markdown";


    const props = defineProps<{
        novelId: string;
        chapterId: string;
    }>();

    const router = useRouter();
    const reader = useReaderStore();
    const progress = useProgressStore();
    const run = useRunStore();

    const id = computed(() => Number(props.novelId));
    const chapterKey = computed(() => Number(props.chapterId));

    const { data: novelData } = useNovel(id);
    const { data: rowData } = useChapters(id);
    const { data: chapterData, isLoading } = useChapter(id, chapterKey);
    const { mutateAsync: startJob, isPending: isStarting } = useStartJob(id);

    const novel = computed(() => novelData.value ?? null);
    const chapter = computed(() => chapterData.value ?? null);
    const rows = computed(() => rowData.value ?? []);

    const scriptLang = computed(() => (novel.value === null ? undefined : scriptLangFor(novel.value.sourceLanguage)));

    const position = computed(() => rows.value.findIndex(row => row.id === chapterKey.value));
    const previous = computed(() => (position.value > 0 ? rows.value[position.value - 1] : undefined));
    const next = computed(() => (position.value >= 0 ? rows.value[position.value + 1] : undefined));

    // Both sides arrive as Markdown and are rendered here rather than in the template, so the parse
    // happens once per chapter instead of on every unrelated re-render.
    // A book can arrive as somebody else's translation with no original anywhere. The reader then
    // shows the one side there is, rather than a blank pane beside it.
    const hasOriginal = computed(() => (chapter.value?.sourceMarkdown ?? null) !== null);

    // The stored preference can say "original" or "both" from a chapter that had one. Honouring it
    // on a chapter that does not would render an empty screen, so the absence of an original decides
    // the mode rather than the preference — and the preference is left untouched for the next one.
    const effectiveMode = computed<ReaderMode>(() => (hasOriginal.value ? reader.mode : "translation"));

    const sourceHtml = computed(() => (chapter.value?.sourceMarkdown == null
        ? ""
        : renderMarkdown(chapter.value.sourceMarkdown)));

    const hasRuby = computed(() => chapter.value?.sourceMarkdown?.includes("<ruby") ?? false);

    const streaming = computed(() => run.deltas[chapterKey.value] ?? null);

    const streamingHtml = computed(() => (streaming.value === null ? "" : renderMarkdown(streaming.value)));

    // A chapter can carry a machine pass, a rendering inherited from the book's own earlier
    // translation, and a hand correction. Showing only the newest would quietly replace the version
    // the reader has been reading for a hundred chapters, so the choice is theirs and it is visible.
    const shownId = ref<number | null>(null);

    const shown = computed(() => {
        const all = chapter.value?.translations ?? [];

        return all.find(translation => translation.id === shownId.value) ?? all[0] ?? null;
    });

    const shownHtml = computed(() => (shown.value === null ? "" : renderMarkdown(shown.value.markdown)));

    const translationOptions = computed(() => (chapter.value?.translations ?? []).map(translation => ({
        value: translation.id,
        label: `${translationOriginLabel(translation.origin)}, ${formatWhen(translation.createdAt)}`,
    })));

    const modeOptions: { label: string; value: ReaderMode }[] = [
        { label: "Original", value: "source" },
        { label: "Both", value: "bilingual" },
        { label: "Translation", value: "translation" },
    ];

    const paneClasses = computed(() => [
        effectiveMode.value,
        effectiveMode.value === "bilingual" ? "" : `align-${reader.align}`,
    ]);

    // Handed to the panes rather than written into the stylesheet: these are the reader's numbers,
    // and the prose rules in main.scss already read them.
    const paneStyle = computed(() => ({
        "--prose-width": `${reader.widthPercent}%`,
        "--prose-offset": `${reader.offsetPercent}%`,
        "--nt-prose-size": `${reader.fontSize}px`,
        "--nt-prose-line": String(reader.lineHeight),
        "--nt-prose-weight": reader.bold ? "600" : "400",
    }));

    const pendingHeadline = computed(() => {
        switch (chapter.value?.translationState) {
            case "Running":
                return "Being translated now. It will appear here when it lands.";
            case "Queued":
                return "Queued. The run is working through the chapters before this one.";
            case "Failed":
                return "This chapter failed. The run carried on without it — try it again on its own.";
            default:
                return "Not translated yet.";
        }
    });


    // Selecting the newest by default is not the same as showing it silently: the picker has to name
    // which rendering is on screen, and it cannot do that with nothing selected.
    watch(
        [chapterKey, () => chapter.value?.translations],
        () => {
            const all = chapter.value?.translations ?? [];
            const stillThere = all.some(translation => translation.id === shownId.value);

            if (!stillThere) {
                shownId.value = all[0]?.id ?? null;
            }
        },
        { immediate: true },
    );


    // Recorded on arrival rather than on leaving: closing the window is the ordinary way to stop
    // reading, and nothing fires reliably then.
    watch(
        () => chapter.value,
        (current) => {
            if (current !== null) {
                progress.record(id.value, current.id, current.index);
            }
        },
        { immediate: true },
    );


    async function translateThisChapter(): Promise<void> {
        await startJob({
            scopeKind: "Single",
            chapterIds: [chapterKey.value],
        });
    }


    function step(target: { id: number } | undefined): void {
        if (target === undefined) {
            return;
        }

        void router.push({ name: "reader", params: { novelId: props.novelId, chapterId: target.id } });
    }


    // Arrow keys page through chapters, but only when the reader is actually reading.
    //
    // These listen on the window, so without a guard every arrow press anywhere in the application
    // turned the page: adjusting a settings slider, moving the caret in the chapter search, editing a
    // parser script. A slider could not be nudged at all — the chapter changed instead, and a run of
    // presses became a run of navigations that looked like the application hanging.
    function ownsArrowKeys(target: EventTarget | null): boolean {
        const element = target as HTMLElement | null;

        if (element === null) {
            return false;
        }

        if (element.isContentEditable) {
            return true;
        }

        if (["INPUT", "TEXTAREA", "SELECT"].includes(element.tagName)) {
            return true;
        }

        // Widgets that steer with the arrow keys themselves. Anything inside an open dialog counts
        // too: while one is up, the page behind it is not what the keys are aimed at.
        return element.closest(
            "[role='slider'], [role='listbox'], [role='menu'], [role='dialog'], [role='tablist'], [role='combobox']",
        ) !== null;
    }


    function pageThrough(event: KeyboardEvent, target: { id: number } | undefined): void {
        // Alt+Arrow is the browser's own back and forward; leave it alone.
        if (event.altKey || event.metaKey || event.ctrlKey || ownsArrowKeys(event.target)) {
            return;
        }

        step(target);
    }


    onKeyStroke("ArrowLeft", event => pageThrough(event, previous.value));
    onKeyStroke("ArrowRight", event => pageThrough(event, next.value));
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .reader {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;
        background: var(--ui-bg-elevated);

        .bar {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.5rem;
            height: $chrome-height;
            padding: 0 1rem 0 0.5rem;
            border-bottom: 1px solid var(--ui-border);
            background: var(--ui-bg);

            .book {
                color: var(--ui-text-muted);
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }

            .spacer {
                flex: 1;
            }

            // The three reading controls belong together and the theme switch does not. Grouping
            // them tightens their spacing and leaves one wider gap before the switch, so the bar
            // reads as two clusters rather than five evenly spaced strangers.
            .reading-controls {
                display: flex;
                align-items: center;
                gap: 0.375rem;
                margin-right: 0.5rem;
            }
        }

        // Both stand in for the panes and take their height, so the footer sits at the bottom of
        // the screen before the text arrives exactly as it does after - it used to hang mid-screen
        // under the skeleton and jump down the moment the chapter loaded.
        .loading,
        .missing {
            flex: 1;
            min-height: 0;
            padding: 3rem 2rem;
        }

        .loading {
            // The reader's own background is the elevated tone the skeleton is drawn in by default,
            // which left the placeholder lines invisible on it - a blank screen rather than a
            // loading one.
            .skeleton {
                height: 1.25rem;
                max-width: $reading-measure-comfortable;
                margin: 0 auto 1rem;
                background: var(--ui-bg-accented);
            }

            .skeleton.wide {
                height: 2rem;
                margin-bottom: 2.5rem;
            }
        }

        .missing p {
            max-width: $reading-measure-comfortable;
            margin: 0 auto;
            color: var(--ui-text-muted);
        }

        .panes {
            flex: 1;
            min-height: 0;
            display: grid;
            overflow: hidden;

            .pane {
                overflow-y: auto;
            }

            // Side by side the page is already divided, so the split is exactly even and the width
            // control does not apply. Each column keeps the same gutter to the spine, which is what
            // makes the pair read as one spread rather than two documents.
            &.bilingual {
                grid-template-columns: 1fr 1fr;

                .pane {
                    padding: 3rem 2.5rem 5rem;
                }

                .source-pane {
                    border-right: 1px solid var(--ui-border);
                }
            }

            // One column, and the reader owns its geometry. No horizontal padding here on purpose:
            // with any, a width of 100% would not reach the edges and the percentages would quietly
            // mean something other than what the sliders say.
            &.source,
            &.translation {
                grid-template-columns: 1fr;

                .pane {
                    padding: 3rem 0 5rem;
                }

                .column {
                    width: var(--prose-width);
                }
            }

            // The offset is measured from the edge the column is pinned to — it pushes the text away
            // from that edge. A centred column has no edge to be pushed from, so it has no offset.
            // `.column` is every block that shares the text's geometry: the prose itself, the line
            // about the rendering above it, the turn to the next chapter below it.
            &.align-center .column {
                margin-inline: auto;
            }

            &.align-left .column {
                margin-left: var(--prose-offset);
                margin-right: auto;
            }

            &.align-right .column {
                margin-left: auto;
                margin-right: var(--prose-offset);
            }

            // One line of fixed height whether it holds a choice of versions or a single label, so
            // the text below starts at the same place on every chapter.
            .meta {
                display: flex;
                align-items: center;
                gap: 1rem;
                height: 2rem;
                margin-bottom: 1.25rem;
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);

                .versions {
                    margin-left: -0.5rem;
                }
            }

            // The one caret in the application. It marks the single place where text is arriving on
            // its own, and it stops when the reader has asked for less motion.
            .streaming .cursor {
                display: inline-block;
                width: 0.5ch;
                height: 1em;
                margin-left: 0.1em;
                vertical-align: text-bottom;
                background: var(--ui-primary);
                animation: caret 1s steps(2, start) infinite;
            }

            .pending {
                max-width: $reading-measure-comfortable;
                margin: 0 auto;

                .headline {
                    margin: 0 0 1rem;
                    font-family: var(--font-prose);
                    font-size: var(--nt-prose-size);
                    line-height: 1.6;
                    color: var(--ui-text-toned);
                }
            }
        }

    }

    @keyframes caret {
        0%,
        100% {
            opacity: 1;
        }

        50% {
            opacity: 0;
        }
    }

    @media (max-width: $breakpoint-lg) {
        .reader .panes.bilingual {
            grid-template-columns: 1fr;
            grid-template-rows: 1fr 1fr;

            .source-pane {
                border-right: 0;
                border-bottom: 1px solid var(--ui-border);
            }
        }
    }
</style>
