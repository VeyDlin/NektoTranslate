<template>
    <UButton
        v-if="canRetry"
        size="xs"
        color="neutral"
        variant="ghost"
        icon="i-material-symbols:refresh-rounded"
        :loading="isPending"
        @click="retry"
    >
        Retry
    </UButton>
</template>

<script setup lang="ts">
    import type { ImportJob, ImportJobItem } from "@/types/models/domain";

    import { computed } from "vue";
    import { useRetryImportItem } from "@/composables/useActivity";


    const props = defineProps<{
        novelId: number;
        job: ImportJob;
        item: ImportJobItem;
    }>();

    // Only a row that actually failed has anything to redo, and only once the run it belongs to has
    // stopped touching it — the server refuses the same race, this just keeps the button from
    // inviting it.
    const canRetry = computed(() => (
        props.item.state === "Failed"
        && (props.job.state === "Completed" || props.job.state === "Failed" || props.job.state === "Cancelled")
    ));

    const { mutateAsync, isPending } = useRetryImportItem(props.novelId);


    async function retry(): Promise<void> {
        await mutateAsync({ jobId: props.job.id, position: props.item.position });
    }
</script>
