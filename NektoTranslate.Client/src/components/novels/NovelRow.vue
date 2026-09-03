<template>
    <RouterLink class="row" :to="{ name: 'novel', params: { novelId: novel.id } }">
        <span class="title" :lang="scriptLangIf(novel.title, scriptLang)">{{ novel.title }}</span>

        <span class="meta">
            <span class="pair">{{ novel.sourceLanguage }} to {{ novel.targetLanguage }}</span>

            <span v-if="progress.total === 0" class="empty">No chapters imported</span>
            <span v-else class="counts">
                {{ formatCount(progress.translated) }} of {{ formatCount(progress.total) }} translated
            </span>

            <span v-if="progress.failed > 0" class="failed">
                {{ formatCount(progress.failed) }} failed
            </span>
        </span>

        <UProgress
            v-if="progress.total > 0"
            class="gauge"
            size="2xs"
            color="success"
            :model-value="progress.translated"
            :max="progress.total"
            :get-value-label="progressLabel"
        />
    </RouterLink>
</template>

<script setup lang="ts">
    import type { Novel } from "@/types/models/domain";
    import { computed, toRef } from "vue";

    import { RouterLink } from "vue-router";
    import { useNovelProgress } from "@/composables/useNovelProgress";
    import { formatCount } from "@/utils/format";
    import { scriptLangFor, scriptLangIf } from "@/utils/language";


    const props = defineProps<{ novel: Novel }>();

    const { progress } = useNovelProgress(toRef(() => props.novel.id));

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
        }

        .gauge {
            position: absolute;
            inset: auto 0 -1px;
        }
    }
</style>
