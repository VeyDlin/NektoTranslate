<template>
    <span
        class="dot"
        :class="state.toLowerCase()"
        :title="translationStateLabel(state)"
        role="img"
        :aria-label="translationStateLabel(state)"
    />
</template>

<script setup lang="ts">
    import type { TranslationState } from "@/types/models/domain";
    import { translationStateLabel } from "@/utils/format";


    defineProps<{ state: TranslationState }>();
</script>

<style scoped lang="scss">
    // A badge per row would be unreadable at two thousand rows and would put a coloured rectangle
    // beside every chapter, which is the opposite of making the failures findable. A dot carries the
    // same five values in a tenth of the ink.
    .dot {
        // Inline-block, not inline: width and height do not apply to an inline box, and this sits
        // inside a table cell where nothing else establishes one.
        display: inline-block;
        flex: none;
        width: 0.5rem;
        height: 0.5rem;
        border-radius: 50%;
        background: var(--ui-text-dimmed);

        &.none {
            background: transparent;
            border: 1px solid var(--ui-text-dimmed);
        }

        &.queued {
            background: var(--ui-text-muted);
        }

        &.running {
            background: var(--ui-primary);
            animation: pulse 1.4s ease-in-out infinite;
        }

        &.translated {
            background: var(--ui-success);
        }

        &.failed {
            background: var(--ui-error);
        }
    }

    // The one piece of motion that is not a response to a click. It marks the single chapter being
    // worked on right now, which is the only thing on the screen changing by itself.
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
