<template>
    <div class="alignment-page">
        <header class="bar">
            <UButton
                :to="{ name: 'novel', params: { novelId } }"
                icon="i-material-symbols:arrow-back-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                aria-label="Back to the novel"
            />

            <span class="book">{{ novel?.title ?? "" }}</span>

            <span class="where">Chapter alignment</span>

            <span class="spacer" />

            <span v-if="selection.size > 0" class="chosen">
                {{ selection.size }} selected · chapters {{ chapterNumber(span.from) }}–{{ chapterNumber(span.to) }}
            </span>
        </header>

        <div class="tools">
            <!-- The usual reason to be here is a translator's note as the site's first entry, which
                 puts every chapter one place late. Saying so is shorter than making the user work out
                 what a bare number beside two buttons does. -->
            <span class="verb">Shift the selected {{ language }} translations by</span>

            <UInputNumber v-model="offset" :min="-500" :max="500" class="offset" />

            <span class="verb">{{ Math.abs(offset) === 1 ? "chapter" : "chapters" }}</span>

            <UButton
                color="neutral"
                variant="subtle"
                :disabled="!canMove"
                :loading="isMoving"
                @click="preview"
            >
                Preview move
            </UButton>

            <UButton :disabled="!canMove || !previewed" :loading="isMoving" @click="apply">
                Move
            </UButton>

            <span class="spacer" />

            <UButton
                color="error"
                variant="subtle"
                :disabled="selection.size === 0"
                :loading="isDeleting"
                @click="confirmingDelete = true"
            >
                Delete translation
            </UButton>
        </div>

        <!-- The one irreversible action on this screen, and the only one that destroys work somebody
             else did. It says which chapters and which language before it happens. -->
        <UModal
            v-model:open="confirmingDelete"
            :title="deleteTitle"
            :description="`The original chapters stay. Only the ${language} translation is removed.`"
        >
            <template #body>
                <div class="confirm">
                    <p v-if="deletableCount > 0">
                        {{ deletableCount }} of the selected chapters
                        {{ deletableCount === 1 ? "carries a" : "carry a" }}
                        {{ language }} translation. This cannot be undone.
                    </p>

                    <p v-else class="none">
                        None of the selected chapters has a {{ language }} translation, so nothing
                        would be removed.
                    </p>

                    <div class="actions">
                        <UButton color="neutral" variant="ghost" @click="confirmingDelete = false">
                            Cancel
                        </UButton>

                        <UButton
                            color="error"
                            :disabled="deletableCount === 0"
                            :loading="isDeleting"
                            @click="removeTranslations"
                        >
                            {{ deletableCount === 1 ? "Delete the translation" : `Delete ${deletableCount} translations` }}
                        </UButton>
                    </div>
                </div>
            </template>
        </UModal>

        <!-- A refusal is the useful answer here, so it gets the room. The list says which chapter is
             in the way of which, rather than only that something is. -->
        <div v-if="collisions.length > 0" class="blocked">
            <span class="heading">Nothing was moved — {{ collisions.length }} in the way</span>

            <ul>
                <li v-for="collision in collisions" :key="collision.fromIndex">
                    Chapter {{ chapterNumber(collision.fromIndex) }} → {{ chapterNumber(collision.targetIndex) }}:
                    {{ describe(collision.reason) }}
                </li>
            </ul>
        </div>

        <p v-else-if="plan !== null" class="planned">
            {{ plan === 1 ? "One translation would move" : `${plan} translations would move` }}.
            Press Move to apply it.
        </p>

        <div class="table">
            <div class="head">
                <span class="cell num">#</span>
                <span class="cell">Original</span>
                <span class="cell">Translation</span>
            </div>

            <div v-if="isLoading" class="empty">Loading…</div>

            <div v-else-if="rows.length === 0" class="empty">
                This novel has no chapters yet.
            </div>

            <!-- Click to select, shift-click for a range, ctrl or cmd to add one. The row is the
                 control: a column of checkboxes would double the clicks for the common case, which
                 is picking a long run of chapters. -->
            <div
                v-for="row in rows"
                :key="row.chapterId"
                class="row"
                :class="{ picked: selection.has(row.index), bare: row.translation === null }"
                @click="onRowClick(row.index, $event)"
            >
                <span class="cell num">{{ chapterNumber(row.index) }}</span>

                <span class="cell">
                    <span class="title">{{ row.title }}</span>
                    <span class="preview">{{ row.source }}</span>
                </span>

                <span class="cell">
                    <template v-if="row.translation">
                        <span class="title">
                            {{ row.translation.origin }}
                            <span v-if="row.versions > 1" class="versions">
                                · {{ row.versions }} versions
                            </span>
                        </span>
                        <span class="preview">{{ row.translation.text }}</span>
                    </template>

                    <span v-else class="preview none">no translation</span>
                </span>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
    import type { AlignmentRow, TranslationCollision } from "@/types/models/domain";

    import { computed, ref, watch } from "vue";
    import { useAlignment, useDeleteTranslations, useMoveTranslations } from "@/composables/useAlignment";
    import { useNovel } from "@/composables/useNovels";
    import { chapterNumber } from "@/utils/format";
    import { describe } from "@/utils/status";


    // Correcting an imported translation that does not line up with the original — the usual cause
    // being a translator's note as the site's first entry, which puts every later chapter one place
    // out.
    const props = defineProps<{ novelId: string }>();

    const id = computed(() => Number(props.novelId));

    const { data: novel } = useNovel(id.value);
    const language = computed(() => novel.value?.targetLanguage ?? "");

    const { data, isLoading } = useAlignment(id.value, language);
    const { mutateAsync: move, isPending: isMoving } = useMoveTranslations(id.value);
    const { mutateAsync: deleteRange, isPending: isDeleting } = useDeleteTranslations(id.value);

    const rows = computed<AlignmentRow[]>(() => data.value ?? []);

    const selection = ref<Set<number>>(new Set());
    const anchor = ref<number | null>(null);
    const offset = ref(-1);

    const collisions = ref<TranslationCollision[]>([]);
    const plan = ref<number | null>(null);
    const previewed = ref(false);
    const confirmingDelete = ref(false);

    // Only the rows that actually hold a translation are at risk, and that is the number the
    // confirmation has to state — a selection of ten chapters where two carry a translation must not
    // read as ten things about to be destroyed.
    const deletableCount = computed(() => rows.value
        .filter(row => selection.value.has(row.index) && row.translation !== null)
        .length);

    const deleteTitle = computed(() => (deletableCount.value === 1
        ? "Delete this translation?"
        : `Delete ${deletableCount.value} translations?`));


    // The API takes a span, so a scattered selection is sent as the span it covers. That is not a
    // fudge: only chapters inside the span that actually hold a translation move, so a gap stays a
    // gap rather than being closed up.
    const span = computed(() => {
        const picked = [...selection.value];

        return {
            from: picked.length === 0 ? 0 : Math.min(...picked),
            to: picked.length === 0 ? 0 : Math.max(...picked),
        };
    });

    const canMove = computed(() => selection.value.size > 0 && offset.value !== 0);


    watch([selection, offset], () => {
        // The old answer described a different move. Leaving it up would invite applying a plan the
        // user is no longer looking at.
        collisions.value = [];
        plan.value = null;
        previewed.value = false;
    }, { deep: true });


    function onRowClick(index: number, event: MouseEvent): void {
        const picked = new Set(selection.value);

        if (event.shiftKey && anchor.value !== null) {
            const from = Math.min(anchor.value, index);
            const to = Math.max(anchor.value, index);

            for (let at = from; at <= to; at++) {
                picked.add(at);
            }
        }
        else if (event.ctrlKey || event.metaKey) {
            if (!picked.delete(index)) {
                picked.add(index);
            }

            anchor.value = index;
        }
        else {
            picked.clear();
            picked.add(index);
            anchor.value = index;
        }

        selection.value = picked;
    }


    async function preview(): Promise<void> {
        const result = await move({
            language: language.value,
            fromIndex: span.value.from,
            toIndex: span.value.to,
            offset: offset.value,
            apply: false,
        });

        collisions.value = result.collisions;
        plan.value = result.collisions.length > 0 ? null : result.moved;
        previewed.value = result.collisions.length === 0;
    }


    async function apply(): Promise<void> {
        const result = await move({
            language: language.value,
            fromIndex: span.value.from,
            toIndex: span.value.to,
            offset: offset.value,
            apply: true,
        });

        collisions.value = result.collisions;
        plan.value = null;
        previewed.value = false;

        if (result.applied) {
            selection.value = new Set();
        }
    }


    async function removeTranslations(): Promise<void> {
        await deleteRange({
            language: language.value,
            from: span.value.from,
            to: span.value.to,
        });

        confirmingDelete.value = false;
        selection.value = new Set();
    }
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .alignment-page {
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

            .chosen {
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);
            }
        }

        .confirm {
            display: flex;
            flex-direction: column;
            gap: 0.75rem;

            p {
                margin: 0;
                color: var(--ui-text-muted);
            }

            .none {
                color: var(--ui-text-dimmed);
            }

            .actions {
                display: flex;
                justify-content: flex-end;
                gap: 0.5rem;
            }
        }

        .tools {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.5rem;
            padding: 0.75rem 1rem;
            border-bottom: 1px solid var(--ui-border);

            .verb {
                color: var(--ui-text-muted);
            }

            .offset {
                width: 8rem;
            }
        }

        .spacer {
            flex: 1;
        }

        .blocked {
            flex: none;
            padding: 0.875rem 1rem;
            border-bottom: 1px solid var(--ui-border);
            background: var(--ui-bg-muted);

            .heading {
                color: var(--ui-error);
            }

            ul {
                margin: 0.5rem 0 0;
                padding-left: 1.25rem;
                font-size: var(--nt-text-sm);
                line-height: 1.7;
                color: var(--ui-text-muted);
            }
        }

        .planned {
            flex: none;
            margin: 0;
            padding: 0.875rem 1rem;
            border-bottom: 1px solid var(--ui-border);
            font-size: var(--nt-text-sm);
            color: var(--ui-text-muted);
        }

        .table {
            flex: 1;
            min-height: 0;
            overflow-y: auto;

            .head,
            .row {
                display: grid;
                grid-template-columns: 4rem 1fr 1fr;
                gap: 1rem;
                padding: 0.625rem 1rem;
                border-bottom: 1px solid var(--ui-border);
            }

            .head {
                position: sticky;
                top: 0;
                z-index: 1;
                background: var(--ui-bg);
                color: var(--ui-text-dimmed);
                font-size: var(--nt-text-sm);
            }

            .row {
                cursor: pointer;
                user-select: none;

                &:hover {
                    background: var(--ui-bg-elevated);
                }

                &.picked {
                    background: var(--ui-bg-accented);
                }

                // A chapter with nothing attached is the thing the user is hunting for, so it is
                // dimmed rather than left looking like a filled row.
                &.bare .cell:last-child {
                    opacity: 0.5;
                }
            }

            .cell {
                display: flex;
                flex-direction: column;
                gap: 0.25rem;
                min-width: 0;

                &.num {
                    color: var(--ui-text-dimmed);
                }

                .title {
                    color: var(--ui-text-highlighted);
                    font-size: var(--nt-text-sm);
                }

                .versions {
                    color: var(--ui-text-dimmed);
                }

                .preview {
                    font-size: var(--nt-text-sm);
                    line-height: 1.5;
                    color: var(--ui-text-muted);
                    overflow: hidden;
                    display: -webkit-box;
                    -webkit-line-clamp: 2;
                    -webkit-box-orient: vertical;

                    &.none {
                        font-style: italic;
                    }
                }
            }

            .empty {
                padding: 2rem 1rem;
                color: var(--ui-text-dimmed);
            }
        }
    }
</style>
