<template>
    <div class="profile">
        <div class="head">
            <h2>Learned voice</h2>

            <UButton
                size="xs"
                color="neutral"
                variant="ghost"
                icon="i-material-symbols:refresh-rounded"
                @click="emit('relearn')"
            >
                Learn again
            </UButton>
        </div>

        <p class="summary">{{ profile.summary }}</p>

        <div class="facts">
            <span>Chapters {{ chapterNumber(profile.fromChapterIndex) }}–{{ chapterNumber(profile.toChapterIndex) }}</span>
            <span v-if="profile.model">{{ profile.model }}</span>
            <span v-if="profile.costUsd !== null">{{ formatCost(profile.costUsd) }}</span>
            <span>{{ formatWhen(profile.createdAt) }}</span>
        </div>
    </div>
</template>

<script setup lang="ts">
    import type { VoiceProfile } from "@/types/models/domain";
    import { chapterNumber, formatCost, formatWhen } from "@/utils/format";


    defineProps<{ profile: VoiceProfile }>();

    const emit = defineEmits<{ relearn: [] }>();
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .profile {
        padding: 1.25rem 1.5rem;
        border-bottom: 1px solid var(--ui-border);

        .head {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 1rem;

            h2 {
                margin: 0;
                font-family: var(--font-prose);
                font-size: var(--nt-text-md);
                font-weight: 400;
                color: var(--ui-text-highlighted);
            }
        }

        // The profile is the model's own text, and a model that was asked for one paragraph still
        // breaks its answer into several when it has several things to say. Those breaks are kept;
        // runs of spaces are not, so the text still wraps like prose.
        .summary {
            max-width: $reading-measure-wide;
            margin: 0.75rem 0 0;
            line-height: 1.6;
            white-space: pre-line;
            color: var(--ui-text-highlighted);
        }

        .facts {
            display: flex;
            flex-wrap: wrap;
            gap: 0.875rem;
            margin-top: 0.75rem;
            font-size: var(--nt-text-sm);
            color: var(--ui-text-muted);
        }
    }
</style>
