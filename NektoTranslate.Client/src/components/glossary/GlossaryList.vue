<template>
    <div class="glossary">
        <div class="controls">
            <UInput
                v-model="search"
                icon="i-material-symbols:search-rounded"
                placeholder="Find a term"
                size="sm"
                class="search"
            />

            <UButton
                :variant="onlyReview ? 'solid' : 'ghost'"
                :color="onlyReview ? 'warning' : 'neutral'"
                size="sm"
                @click="onlyReview = !onlyReview"
            >
                Needs review
                <UBadge v-if="reviewCount > 0" :color="onlyReview ? 'neutral' : 'warning'" variant="subtle" size="sm">
                    {{ reviewCount }}
                </UBadge>
            </UButton>

            <UButton size="sm" icon="i-material-symbols:add-rounded" @click="startAdding">
                Add term
            </UButton>
        </div>

        <p v-if="visible.length === 0" class="blank">
            <template v-if="entries.length === 0">
                No terms yet. The agent records names and terms as it reads each chapter, and you can
                add one yourself at any time.
            </template>
            <template v-else>
                No term matches that.
            </template>
        </p>

        <ul v-else class="entries">
            <li v-for="entry in visible" :key="entry.id" class="entry" :class="{ review: entry.needsReview }">
                <div class="rendering">
                    <span class="source" :lang="scriptLangIf(entry.sourceTerm, scriptLang)">{{ entry.sourceTerm }}</span>
                    <UIcon name="i-material-symbols:arrow-right-alt-rounded" class="arrow" />
                    <span class="target">{{ entry.targetTerm }}</span>

                    <span class="actions">
                        <UButton
                            v-if="entry.needsReview"
                            size="xs"
                            color="warning"
                            variant="subtle"
                            icon="i-material-symbols:check-rounded"
                            :loading="approvingId === entry.id"
                            @click="approve(entry)"
                        >
                            Confirm
                        </UButton>

                        <UButton
                            size="xs"
                            color="neutral"
                            variant="ghost"
                            icon="i-material-symbols:edit-outline-rounded"
                            :aria-label="`Edit ${entry.sourceTerm}`"
                            @click="startEditing(entry)"
                        />

                        <UButton
                            size="xs"
                            color="neutral"
                            variant="ghost"
                            icon="i-material-symbols:delete-outline-rounded"
                            :aria-label="`Delete ${entry.sourceTerm}`"
                            @click="doomed = entry"
                        />
                    </span>
                </div>

                <div class="facts">
                    <span class="category">{{ glossaryCategoryLabel(entry.category) }}</span>

                    <UBadge
                        size="sm"
                        variant="subtle"
                        :color="entry.origin === 'AiExtracted' ? 'warning' : 'neutral'"
                    >
                        {{ glossaryOriginLabel(entry.origin) }}
                    </UBadge>

                    <UBadge v-if="entry.needsReview" size="sm" variant="solid" color="warning">
                        Not confirmed
                    </UBadge>
                </div>

                <p v-if="entry.notes" class="notes">{{ entry.notes }}</p>

                <p v-if="entry.aliases.length > 0" class="aliases">
                    Also written <span :lang="scriptLangIf(entry.aliases.join('、'), scriptLang)">{{ entry.aliases.join("、") }}</span>
                </p>

                <p v-if="containedIn(entry)" class="contained">
                    Also sits inside <span :lang="scriptLangIf(containedIn(entry) ?? '', scriptLang)">{{ containedIn(entry) }}</span>
                </p>
            </li>
        </ul>

        <GlossaryEntryModal
            v-model:open="editing"
            :novel-id="novelId"
            :entry="edited"
            :language="language"
            :script-lang="scriptLang"
        />

        <UModal
            :open="doomed !== null"
            :title="`Delete ${doomed?.sourceTerm ?? ''}?`"
            description="The agent may record it again the next time it reads a chapter containing the term."
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
    import type { GlossaryEntry } from "@/types/models/domain";

    import { computed, ref } from "vue";
    import { useApproveGlossaryEntry, useDeleteGlossaryEntry } from "@/composables/useGlossary";
    import { glossaryCategoryLabel, glossaryOriginLabel } from "@/utils/format";
    import { scriptLangIf } from "@/utils/language";
    import GlossaryEntryModal from "./GlossaryEntryModal.vue";


    const props = defineProps<{
        entries: GlossaryEntry[];
        novelId: number;
        language: string;
        scriptLang?: string;
    }>();

    const search = ref("");
    const onlyReview = ref(false);
    const editing = ref(false);
    const edited = ref<GlossaryEntry | null>(null);
    const doomed = ref<GlossaryEntry | null>(null);
    const approvingId = ref<number | null>(null);

    const { mutateAsync: approveEntry } = useApproveGlossaryEntry(() => props.novelId);
    const { mutateAsync: deleteEntry, isPending: isDeleting } = useDeleteGlossaryEntry(() => props.novelId);

    const reviewCount = computed(() => props.entries.filter(entry => entry.needsReview).length);

    // The server already returns these longest source term first, so the order is left alone here.
    const visible = computed(() => {
        const needle = search.value.trim().toLowerCase();

        return props.entries.filter((entry) => {
            if (onlyReview.value && !entry.needsReview) {
                return false;
            }

            if (needle === "") {
                return true;
            }

            return entry.sourceTerm.toLowerCase().includes(needle)
                || entry.targetTerm.toLowerCase().includes(needle)
                || entry.aliases.some(alias => alias.toLowerCase().includes(needle));
        });
    });


    // Sorting alone leaves the reader to notice the overlap. Naming it removes the guesswork about
    // whether two similar entries are a duplicate or a deliberate distinction.
    function containedIn(entry: GlossaryEntry): string | null {
        const container = props.entries.find(candidate => (
            candidate.id !== entry.id
            && candidate.sourceTerm.length > entry.sourceTerm.length
            && candidate.sourceTerm.includes(entry.sourceTerm)
        ));

        return container?.sourceTerm ?? null;
    }


    function startAdding(): void {
        edited.value = null;
        editing.value = true;
    }


    function startEditing(entry: GlossaryEntry): void {
        edited.value = entry;
        editing.value = true;
    }


    // Confirming is not editing. It clears the review flag and leaves the origin alone, so the entry
    // still says the model chose this rendering — which stays true after someone agrees with it.
    async function approve(entry: GlossaryEntry): Promise<void> {
        approvingId.value = entry.id;

        try {
            await approveEntry(entry.id);
        }
        finally {
            approvingId.value = null;
        }
    }


    async function confirmDelete(): Promise<void> {
        if (doomed.value === null) {
            return;
        }

        await deleteEntry(doomed.value.id);
        doomed.value = null;
    }
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .glossary {
        display: flex;
        flex-direction: column;
        min-height: 0;

        .controls {
            display: flex;
            align-items: center;
            gap: 0.75rem;
            padding: 0.75rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);

            .search {
                flex: 1;
                min-width: 0;
            }
        }

        .blank {
            max-width: 34rem;
            margin: 3rem 1.5rem;
            color: var(--ui-text-muted);
        }

        .entries {
            flex: 1;
            min-height: 0;
            overflow-y: auto;
            margin: 0;
            padding: 0;
            list-style: none;
        }

        .entry {
            padding: 1rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);

            // The only structural device that carries meaning here: an unconfirmed rendering is
            // marked down the edge, so scanning the column finds every invention at once.
            &.review {
                box-shadow: inset 2px 0 0 var(--ui-warning);
            }

            .rendering {
                display: flex;
                align-items: baseline;
                gap: 0.625rem;

                .source {
                    font-size: var(--nt-text-md);
                    color: var(--ui-text-highlighted);
                }

                .arrow {
                    width: 1rem;
                    height: 1rem;
                    color: var(--ui-text-dimmed);
                }

                .target {
                    font-family: var(--font-prose);
                    font-size: var(--nt-text-md);
                    color: var(--ui-text-highlighted);
                }

                // Held out of the reading rhythm until wanted. The row is something to scan, and a
                // column of buttons down the page competes with the terms themselves.
                .actions {
                    display: flex;
                    align-items: center;
                    gap: 0.25rem;
                    margin-left: auto;
                    opacity: 0;
                    transition: opacity 0.15s ease-out;
                }
            }

            &:hover .actions,
            &:focus-within .actions {
                opacity: 1;
            }

            .facts {
                display: flex;
                flex-wrap: wrap;
                align-items: center;
                gap: 0.625rem;
                margin-top: 0.5rem;
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);

                .category {
                    margin-right: 0.25rem;
                }
            }

            .notes,
            .aliases,
            .contained {
                margin: 0.375rem 0 0;
                max-width: $reading-measure-comfortable;
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);
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
