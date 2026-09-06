<template>
    <UApp>
        <DefaultLayout />
    </UApp>
</template>

<script setup lang="ts">
    import { useToast } from "@nuxt/ui/composables";
    import DefaultLayout from "@/layouts/DefaultLayout.vue";
    import { registerToaster } from "@/utils/notify";


    // The toaster is a composable and needs a component to be created from; this is the first one
    // there is. Handed over once, so the query client's error hooks - which run with no component in
    // sight - can still put a sentence in front of the user.
    registerToaster(useToast());
</script>
