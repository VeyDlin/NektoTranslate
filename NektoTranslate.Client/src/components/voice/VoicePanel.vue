<template>
    <div class="voice">
        <div v-if="profile === null || profile === undefined" class="blank">
            <h1>No voice learned yet</h1>
            <p>
                Learning reads a range of chapters that already carry a translation and writes a
                short description of how that translation is written — register, rhythm, the small
                habits a human translator repeats without deciding to each time. Every translation
                and repair after that can follow it instead of drifting chapter to chapter. Where a
                chapter also has its original, what it teaches about names and terms is recorded in
                the Glossary tab instead of here.
            </p>
            <p v-if="!hasTranslatedChapters" class="requirement">
                Needs at least one translated chapter to learn from. Translate part of the book
                first.
            </p>
            <UButton
                icon="i-material-symbols:record-voice-over-outline-rounded"
                :disabled="!hasTranslatedChapters"
                @click="emit('learn')"
            >
                Learn from the translation
            </UButton>
        </div>

        <template v-else>
            <VoiceProfileCard :profile="profile" @relearn="emit('learn')" />

            <div class="controls">
                <UInput
                    v-model="search"
                    icon="i-material-symbols:search-rounded"
                    placeholder="Find a term"
                    size="sm"
                    class="search"
                />

                <span class="found">{{ formatCount(visible.length) }} of {{ formatCount(terms.length) }} terms</span>
            </div>

            <div class="list">
                <p v-if="terms.length === 0" class="empty">
                    No terms recorded from the learned range yet.
                </p>

                <p v-else-if="visible.length === 0" class="empty">
                    No term matches that.
                </p>

                <ul v-else class="terms">
                    <li v-for="term in visible" :key="term.id" class="term">
                        <div class="heading">
                            <span class="name">{{ term.term }}</span>

                            <span class="count">
                                {{ formatCount(term.occurrences) }} {{ term.occurrences === 1 ? "occurrence" : "occurrences" }}
                            </span>

                            <!-- A plain button rather than a UButton: there are hundreds of these rows,
                                 and a component instance per row was a visible part of the pause
                                 when the tab opened. -->
                            <button
                                type="button"
                                class="delete"
                                :aria-label="`Delete ${term.term}`"
                                @click="doomed = term"
                            >
                                <UIcon name="i-material-symbols:delete-outline-rounded" />
                            </button>
                        </div>

                        <p class="category">{{ glossaryCategoryLabel(term.category) }}</p>

                        <p v-if="term.notes" class="notes">{{ term.notes }}</p>

                        <p v-if="term.variants.length > 0" class="variants">
                            Also written {{ term.variants.join(", ") }}
                        </p>
                    </li>
                </ul>
            </div>
        </template>

        <UModal
            :open="doomed !== null"
            :title="`Delete ${doomed?.term ?? ''}?`"
            description="The next learn pass may record it again if the range it reads still contains the term."
            @update:open="(open: boolean) => !open && (doomed = null)"
        >
            <template #footer>
                <div class="confirm">
                    <UButton color="neutral" variant="ghost" @click="doomed = null">
                        Keep it
                    </UButton>

                    <UButton color="error" :loading="isDeleting" @click="confirmDelete">
                        Delete
                    </UButton>
                </div>
            </template>
        </UModal>
    </div>
</template>

<script setup lang="ts">
    import type { ChapterSummary, TranslationTerm } from "@/types/models/domain";

    import { computed, ref } from "vue";
    import { useDeleteTerm, useVoiceProfile, useVoiceTerms } from "@/composables/useVoice";
    import { formatCount, glossaryCategoryLabel } from "@/utils/format";
    import VoiceProfileCard from "./VoiceProfileCard.vue";


    const props = defineProps<{
        novelId: number;
        language: string;
        rows: ChapterSummary[];
    }>();

    const emit = defineEmits<{ learn: [] }>();

    const search = ref("");
    const doomed = ref<TranslationTerm | null>(null);

    const { data: profileData } = useVoiceProfile(() => props.novelId, () => props.language);
    const { data: termsData } = useVoiceTerms(() => props.novelId, () => props.language);
    const { mutateAsync: deleteTerm, isPending: isDeleting } = useDeleteTerm(
        () => props.novelId,
        () => props.language,
    );

    const profile = computed(() => profileData.value);

    const hasTranslatedChapters = computed(() => props.rows.some(row => row.translationState === "Translated"));

    // Most-seen first: a term that shows up three hundred times and one seen once are not the same
    // kind of fact about the book, and the order is what makes that legible at a glance.
    const terms = computed(() => [...(termsData.value ?? [])].sort((left, right) => right.occurrences - left.occurrences));

    const visible = computed(() => {
        const needle = search.value.trim().toLowerCase();

        if (needle === "") {
            return terms.value;
        }

        return terms.value.filter(term => (
            term.term.toLowerCase().includes(needle)
            || term.variants.some(variant => variant.toLowerCase().includes(needle))
        ));
    });


    async function confirmDelete(): Promise<void> {
        if (doomed.value === null) {
            return;
        }

        await deleteTerm(doomed.value.id);
        doomed.value = null;
    }
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    // The panel scrolls as one page. A learned profile can run to a couple of screens on its own,
    // and a list that scrolled inside a fixed-height panel was left with no height at all under a
    // profile that tall - the terms were there and could not be reached. Now the profile, the
    // search and the terms flow one after another and the whole thing scrolls.
    .voice {
        flex: 1;
        min-height: 0;
        overflow-y: auto;

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

            .requirement {
                color: var(--ui-warning);
            }
        }

        // Stays at the top while the terms scroll under it, so the search is always at hand
        // without the panel keeping a separate scroll region for the list.
        .controls {
            position: sticky;
            top: 0;
            z-index: 1;
            display: flex;
            align-items: center;
            gap: 0.75rem;
            padding: 0.75rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);
            background: var(--ui-bg);

            .search {
                flex: 1;
                min-width: 0;
            }

            .found {
                flex: none;
                color: var(--ui-text-muted);
            }
        }

        .list {
            .empty {
                max-width: 34rem;
                margin: 3rem 1.5rem;
                color: var(--ui-text-muted);
            }

            .terms {
                margin: 0;
                padding: 0;
                list-style: none;
            }

            // Rows below the fold are neither laid out nor painted until they scroll into view. A
            // book learns a few hundred terms, and laying out every one of them at once is what
            // made opening this tab pause; the reserved height keeps the scrollbar honest meanwhile.
            .term {
                padding: 1rem 1.5rem;
                border-bottom: 1px solid var(--ui-border);
                content-visibility: auto;
                contain-intrinsic-size: auto 5.5rem;

                .heading {
                    display: flex;
                    align-items: baseline;
                    gap: 0.625rem;

                    .name {
                        font-family: var(--font-prose);
                        font-size: var(--nt-text-md);
                        color: var(--ui-text-highlighted);
                    }

                    .count {
                        color: var(--ui-text-muted);
                    }

                    // Held out of the reading rhythm until wanted, the same restraint the glossary
                    // list uses for its own row actions. Drawn to the size and shape of the ghost
                    // buttons elsewhere, so it does not read as a different kind of control.
                    .delete {
                        display: inline-flex;
                        align-items: center;
                        justify-content: center;
                        width: 1.75rem;
                        height: 1.75rem;
                        margin-left: auto;
                        padding: 0;
                        border: 0;
                        border-radius: 0.375rem;
                        background: none;
                        color: var(--ui-text-muted);
                        cursor: pointer;
                        opacity: 0;
                        transition: opacity 0.15s ease-out;

                        &:hover {
                            background: var(--ui-bg-elevated);
                            color: var(--ui-text-highlighted);
                        }

                        &:focus-visible {
                            outline: 2px solid var(--ui-primary);
                            outline-offset: 1px;
                            opacity: 1;
                        }

                        span {
                            width: 1rem;
                            height: 1rem;
                        }
                    }
                }

                &:hover .delete,
                &:focus-within .delete {
                    opacity: 1;
                }

                .category {
                    margin: 0.375rem 0 0;
                    font-size: var(--nt-text-sm);
                    color: var(--ui-text-muted);
                }

                .notes,
                .variants {
                    margin: 0.375rem 0 0;
                    max-width: $reading-measure-comfortable;
                    font-size: var(--nt-text-sm);
                    color: var(--ui-text-muted);
                }
            }
        }
    }

    .confirm {
        display: flex;
        justify-content: flex-end;
        gap: 0.5rem;
        width: 100%;
    }
</style>
