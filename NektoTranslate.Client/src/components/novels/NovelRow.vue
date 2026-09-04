<template>
    <RouterLink class="row" :to="{ name: 'novel', params: { novelId: novel.id } }">
        <span class="title" :lang="scriptLangIf(novel.title, scriptLang)">{{ novel.title }}</span>

        <span class="meta">
            <span class="pair">{{ novel.sourceLanguage }} to {{ novel.targetLanguage }}</span>

            <span v-if="novel.totalChapters === 0" class="empty">No chapters imported</span>
            <span v-else class="counts">
                {{ formatCount(novel.translatedChapters) }} of {{ formatCount(novel.totalChapters) }} translated
            </span>

            <span v-if="novel.failedChapters > 0" class="failed">
                {{ formatCount(novel.failedChapters) }} failed
            </span>

            <span v-if="novel.isActive" class="running">
                <span class="pulse" />
                Running
            </span>
        </span>

        <UProgress
            v-if="novel.totalChapters > 0"
            class="gauge"
            size="2xs"
            color="success"
            :model-value="novel.translatedChapters"
            :max="novel.totalChapters"
            :get-value-label="progressLabel"
        />
    </RouterLink>
</template>

<script setup lang="ts">
    import type { NovelListItem } from "@/types/models/domain";
    import { computed } from "vue";

    import { RouterLink } from "vue-router";
    import { formatCount } from "@/utils/format";
    import { scriptLangFor, scriptLangIf } from "@/utils/language";


    const props = defineProps<{ novel: NovelListItem }>();

    const scriptLang = computed(() => scriptLangFor(props.novel.sourceLanguage));


    function progressLabel(value: number | null | undefined, max: number): string {
        return `${formatCount(value ?? 0)} of ${formatCount(max)} chapters translated`;
    }
</script>

<style scoped lang="scss">
    .row {
        position: relative;
        display: block;
        padding: 1.5rem 0 1.625rem;
        border-bottom: 1px solid var(--ui-border);
        text-decoration: none;
        color: inherit;

        &:hover .title {
            color: var(--ui-primary);
        }

        .title {
            display: block;
            font-size: var(--nt-text-xl);
            line-height: 1.3;
            color: var(--ui-text-highlighted);
            transition: color 0.15s ease-out;
        }

        .meta {
            display: flex;
            flex-wrap: wrap;
            gap: 1.25rem;
            margin-top: 0.5rem;
            color: var(--ui-text-muted);

            .failed {
                color: var(--ui-error);
            }

            .running {
                display: inline-flex;
                align-items: center;
                gap: 0.375rem;
                color: var(--ui-primary);

                .pulse {
                    width: 0.5rem;
                    height: 0.5rem;
                    border-radius: 50%;
                    background: var(--ui-primary);
                    animation: pulse 1.4s ease-in-out infinite;
                }
            }
        }

        .gauge {
            position: absolute;
            inset: auto 0 -1px;
        }
    }

    // The one piece of motion here that is not a response to a click - it marks a book with a run
    // in progress, the same animation ChapterStateDot uses for a chapter being worked on right now.
    @keyframes pulse {
        0%,
        100% {
            opacity: 1;
        }

        50% {
            opacity: 0.35;
        }
    }
</style>
