<template>
    <UModal v-model:open="open" :title="title" :description="description" :dismissible="!isDeleting">
        <template #body>
            <div class="body">
                <!-- Named, not counted. The usual reason to be here is that an import went wrong, and
                     the only way to be sure the right rows are about to go is to read them back. -->
                <ul class="list">
                    <li v-for="chapter in shown" :key="chapter.id">
                        <span class="num">{{ chapterNumber(chapter.index) }}</span>
                        <span class="title">{{ chapter.title }}</span>
                        <span v-if="chapter.translationState === 'Translated'" class="translated">
                            has a translation
                        </span>
                    </li>
                </ul>

                <p v-if="pending.length > shown.length" class="more">
                    and {{ formatCount(pending.length - shown.length) }} more
                </p>

                <p v-if="translatedCount > 0" class="warning">
                    {{ formatCount(translatedCount) }} of them
                    {{ translatedCount === 1 ? "carries a translation that goes" : "carry translations that go" }}
                    with the chapter. This cannot be undone.
                </p>

                <p v-else class="note">
                    This cannot be undone.
                </p>

                <p v-if="failed.length > 0" class="failure">
                    {{ formatCount(failed.length) }} could not be deleted. They are still in the list.
                </p>

                <div class="actions">
                    <UButton color="neutral" variant="ghost" :disabled="isDeleting" @click="open = false">
                        Cancel
                    </UButton>

                    <UButton color="error" :loading="isDeleting" @click="confirm">
                        {{ deleteLabel }}
                    </UButton>
                </div>
            </div>
        </template>
    </UModal>
</template>

<script setup lang="ts">
    import type { ChapterSummary } from "@/types/models/domain";

    import { computed, ref, watch } from "vue";
    import { useDeleteChapter } from "@/composables/useChapters";
    import { chapterNumber, formatCount } from "@/utils/format";


    const props = defineProps<{
        novelId: number;
        chapters: ChapterSummary[];
    }>();

    const emit = defineEmits<{ deleted: [] }>();

    const open = defineModel<boolean>("open", { required: true });

    const { mutateAsync: removeChapter } = useDeleteChapter(() => props.novelId);

    const isDeleting = ref(false);
    const failed = ref<number[]>([]);

    // Taken once, when the dialog opens, and read from there afterwards. Rendering the live selection
    // would rewrite the dialog underneath the user as the rows it names disappear — closing on
    // "Delete 0 chapters?" after they pressed "Delete the chapter".
    const pending = ref<ChapterSummary[]>([]);

    // Long enough to recognise the run being removed, short enough that the confirmation still fits on
    // screen with its buttons visible — a list that scrolls the actions out of view is a dialog people
    // dismiss without reading.
    const shown = computed(() => pending.value.slice(0, 12));

    const translatedCount = computed(() => pending.value.filter(row => row.translationState === "Translated").length);

    const title = computed(() => (pending.value.length === 1
        ? "Delete this chapter?"
        : `Delete ${formatCount(pending.value.length)} chapters?`));

    const description = computed(() => (pending.value.length === 1
        ? "The chapter and everything translated from it are removed."
        : "The chapters and everything translated from them are removed."));

    const deleteLabel = computed(() => (pending.value.length === 1
        ? "Delete the chapter"
        : `Delete ${formatCount(pending.value.length)} chapters`));


    watch(open, (isOpen) => {
        if (isOpen) {
            failed.value = [];
            pending.value = [...props.chapters];
        }
    });


    // One request per chapter: the API deletes by id, and a partial failure has to leave the rows it
    // could not remove in the list rather than reporting a success the chapter list would contradict.
    async function confirm(): Promise<void> {
        isDeleting.value = true;
        failed.value = [];

        try {
            for (const chapter of pending.value) {
                try {
                    await removeChapter(chapter.id);
                }
                catch {
                    failed.value = [...failed.value, chapter.id];
                }
            }
        }
        finally {
            isDeleting.value = false;
        }

        if (failed.value.length === 0) {
            emit("deleted");
            open.value = false;
        }
    }
</script>

<style scoped lang="scss">
    .body {
        display: flex;
        flex-direction: column;
        gap: 0.75rem;

        .list {
            max-height: 16rem;
            margin: 0;
            padding: 0;
            overflow-y: auto;
            list-style: none;

            li {
                display: flex;
                align-items: baseline;
                gap: 0.75rem;
                padding: 0.25rem 0;
            }

            .num {
                flex: none;
                width: 2.5rem;
                text-align: right;
                color: var(--ui-text-dimmed);
                font-variant-numeric: tabular-nums;
            }

            .title {
                flex: 1;
                min-width: 0;
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
            }

            .translated {
                flex: none;
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);
            }
        }

        .more,
        .note {
            margin: 0;
            color: var(--ui-text-muted);
        }

        .warning {
            margin: 0;
            color: var(--ui-warning);
        }

        .failure {
            margin: 0;
            color: var(--ui-error);
        }

        .actions {
            display: flex;
            justify-content: flex-end;
            gap: 0.5rem;
            margin-top: 0.5rem;
        }
    }
</style>
