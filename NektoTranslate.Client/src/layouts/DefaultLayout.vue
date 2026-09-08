<template>
    <div class="shell">
        <WindowChrome />

        <main class="content">
            <RouterView />
        </main>

        <RunStrip />
    </div>
</template>

<script setup lang="ts">
    import { computed } from "vue";
    import { RouterView, useRoute } from "vue-router";

    import RunStrip from "@/components/common/RunStrip.vue";
    import WindowChrome from "@/components/common/WindowChrome.vue";
    import { useTranslationStream } from "@/composables/useTranslationStream";


    const route = useRoute();

    // The stream is watched here rather than inside the novel screen. Walking off to read a chapter
    // unmounts that screen, and a run whose progress stopped updating the moment the user started
    // reading would break the one promise this application makes.
    const watchedNovelId = computed(() => {
        const raw = route.params.novelId;
        const value = Array.isArray(raw) ? raw[0] : raw;

        return value === undefined ? null : Number(value);
    });

    useTranslationStream(watchedNovelId);
</script>

<style scoped lang="scss">
    .shell {
        display: flex;
        flex-direction: column;
        height: 100%;
        background: var(--ui-bg);

        .content {
            flex: 1;
            min-height: 0;
            display: flex;
            flex-direction: column;
        }
    }
</style>
