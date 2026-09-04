<template>
    <UBadge :label="importItemStateLabel(state)" :color="color" variant="subtle" size="sm" />
</template>

<script setup lang="ts">
    import type { ImportItemState } from "@/types/models/domain";

    import { computed } from "vue";
    import { importItemStateLabel } from "@/utils/format";


    const props = defineProps<{ state: ImportItemState }>();

    const color = computed(() => {
        switch (props.state) {
            case "Imported":
                return "success";
            case "Skipped":
                return "warning";
            case "Failed":
                return "error";
            default:
                return "neutral";
        }
    });
</script>
