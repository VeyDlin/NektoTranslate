<template>
    <UButton
        :to="to"
        :icon="icon"
        size="sm"
        color="neutral"
        :variant="active ? 'subtle' : 'ghost'"
        class="action"
        :class="{ paused: job?.state === 'Paused' }"
    >
        {{ label }}

        <span v-if="active && job !== null && job.totalCount > 0" class="counts">
            {{ formatCount(job.processedCount) }}/{{ formatCount(job.totalCount) }}
        </span>

        <!-- The button is the way back to the screen doing the work, so it carries the work's state:
             a bar across its foot rather than a number somewhere else on the page. -->
        <span v-if="active" class="gauge" :style="{ width: `${percent}%` }" />
    </UButton>
</template>

<script setup lang="ts">
    import type { RouteLocationRaw } from "vue-router";
    import type { ImportJob } from "@/types/models/domain";

    import { computed } from "vue";
    import { formatCount } from "@/utils/format";


    const props = defineProps<{
        to: RouteLocationRaw;
        icon: string;
        label: string;
        job: ImportJob | null;
    }>();

    const active = computed(() => props.job !== null && (
        props.job.state === "Queued" || props.job.state === "Running" || props.job.state === "Paused"
    ));

    const percent = computed(() => {
        if (props.job === null || props.job.totalCount === 0) {
            return 0;
        }

        return Math.round((props.job.processedCount / props.job.totalCount) * 100);
    });
</script>

<style scoped lang="scss">
    .action {
        position: relative;
        overflow: hidden;

        .counts {
            margin-left: 0.375rem;
            color: var(--ui-text-muted);
            font-variant-numeric: tabular-nums;
        }

        .gauge {
            position: absolute;
            left: 0;
            bottom: 0;
            height: 2px;
            background: var(--ui-primary);
            transition: width 0.3s ease-out;
        }

        &.paused .gauge {
            background: var(--ui-warning);
        }
    }
</style>
