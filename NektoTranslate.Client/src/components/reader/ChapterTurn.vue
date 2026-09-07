<template>
    <nav class="turn" aria-label="Neighbouring chapters">
        <RouterLink
            v-if="previous"
            class="step"
            :to="{ name: 'reader', params: { novelId, chapterId: previous.id } }"
        >
            <UIcon name="i-material-symbols:chevron-left-rounded" class="chev" />
            <span class="index">{{ chapterNumber(previous.index) }}</span>
            <span class="title" :lang="scriptLangIf(previous.title, scriptLang)">{{ previous.title }}</span>
            <ChapterStateDot :state="previous.translationState" />
        </RouterLink>
        <span v-else class="step" />

        <RouterLink
            v-if="next"
            class="step next"
            :to="{ name: 'reader', params: { novelId, chapterId: next.id } }"
        >
            <ChapterStateDot :state="next.translationState" />
            <span class="index">{{ chapterNumber(next.index) }}</span>
            <span class="title" :lang="scriptLangIf(next.title, scriptLang)">{{ next.title }}</span>
            <UIcon name="i-material-symbols:chevron-right-rounded" class="chev" />
        </RouterLink>
        <span v-else class="step" />
    </nav>
</template>

<script setup lang="ts">
    import type { ChapterSummary } from "@/types/models/domain";
    import { RouterLink } from "vue-router";
    import ChapterStateDot from "@/components/chapters/ChapterStateDot.vue";
    import { chapterNumber } from "@/utils/format";
    import { scriptLangIf } from "@/utils/language";


    // The turn of the page at the end of a chapter: where the reader came from and where the book
    // goes next, as two links of one shape. It sits after the last paragraph rather than pinned to
    // the bottom of the screen, because that is where a reader who has just finished the chapter is
    // looking - and a bar fixed under a half-read page is chrome, not part of the book. The dots
    // carry what the chapter list would have shown: whether the neighbour is readable yet.
    defineProps<{
        // A route parameter where the reader has it, a number where the list does; the router takes
        // either.
        novelId: number | string;
        previous?: ChapterSummary;
        next?: ChapterSummary;
        scriptLang?: string;
    }>();
</script>

<style scoped lang="scss">
    .turn {
        display: flex;
        justify-content: space-between;
        gap: 2rem;
        margin-top: 3rem;
        padding-top: 1.25rem;
        border-top: 1px solid var(--ui-border);
        font-size: var(--nt-text-sm);

        // Each side is a link and nothing more - no button chrome, no half-width block. The empty
        // span at either end of the book keeps the other side in its place.
        .step {
            display: inline-flex;
            align-items: center;
            gap: 0.5rem;
            min-width: 0;
            max-width: 48%;
            text-decoration: none;
            color: var(--ui-text-muted);

            &:hover {
                color: var(--ui-primary);
            }

            &.next {
                justify-content: flex-end;
            }

            .index,
            .chev {
                flex: none;
            }

            .title {
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
            }

            .chev {
                width: 1.125rem;
                height: 1.125rem;
            }
        }
    }
</style>
