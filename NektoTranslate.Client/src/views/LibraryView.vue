<template>
    <div class="library">
        <AppBar no-back>
            <div class="title" data-bar-text>
                <span class="wordmark">NektoTranslate</span>
                <span
                    v-if="versionDisplay !== null"
                    class="version"
                    :title="`Version ${health?.version}`"
                >{{ versionDisplay }}</span>
            </div>

            <div class="tools">
                <UButton size="sm" icon="i-material-symbols:add-rounded" @click="creating = true">
                    Add novel
                </UButton>

                <UButton
                    :to="{ name: 'settings' }"
                    icon="i-material-symbols:settings-outline-rounded"
                    color="neutral"
                    variant="ghost"
                    size="sm"
                    aria-label="Application settings"
                />

                <UColorModeButton size="sm" />
            </div>
        </AppBar>

        <div class="page">
            <div v-if="isLoading" class="placeholder">
                <USkeleton v-for="index in 3" :key="index" class="skeleton" />
            </div>

            <p v-else-if="isError" class="failure">
                Could not read the library. The server did not answer.
            </p>

            <div v-else-if="novels.length === 0" class="blank">
                <h1>Nothing on the shelf yet</h1>
                <p>
                    Add a novel, paste its first chapter, and the agent will translate it while you
                    read what is already done.
                </p>
                <UButton icon="i-material-symbols:add-rounded" @click="creating = true">
                    Add novel
                </UButton>
            </div>

            <div v-else class="shelf">
                <NovelRow v-for="novel in novels" :key="novel.id" :novel="novel" />
            </div>
        </div>

        <CreateNovelModal v-model:open="creating" />
    </div>
</template>

<script setup lang="ts">
    import { computed, ref } from "vue";

    import AppBar from "@/components/common/AppBar.vue";
    import CreateNovelModal from "@/components/novels/CreateNovelModal.vue";
    import NovelRow from "@/components/novels/NovelRow.vue";
    import { useNovels } from "@/composables/useNovels";
    import { useHealth } from "@/composables/useSystem";


    const creating = ref(false);
    const { data, isLoading, isError } = useNovels();
    const { data: health } = useHealth();

    const novels = computed(() => data.value ?? []);

    // The dimmed number beside the wordmark is the plain version; the full informational one -
    // "0.1.0+<sha>" once CI has stamped a build - stays in the title tooltip only, since it means
    // little at a glance and would just be noise printed next to the application's own name.
    const versionDisplay = computed(() => health.value?.version.split("+")[0] ?? null);
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .library {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;

        .bar {
            .title {
                display: flex;
                align-items: baseline;
                gap: 0.5rem;
            }

            .wordmark {
                font-weight: 500;
                letter-spacing: 0.01em;
                color: var(--ui-text-muted);
            }

            .version {
                font-size: var(--nt-text-sm);
                color: var(--ui-text-dimmed);
            }

            // AppBar's own bar leaves whatever gap the shared style sets between the title and this
            // group - pushed to the far edge itself rather than by that gap, the same as it read
            // before this bar became AppBar's, and the push holds regardless of whether the window
            // controls sit after it too.
            .tools {
                display: flex;
                align-items: center;
                gap: 0.25rem;
                margin-left: auto;
            }
        }

        .page {
            flex: 1;
            min-height: 0;
            overflow-y: auto;
            padding: 3rem 1.5rem 4rem;
        }

        .shelf,
        .placeholder,
        .blank,
        .failure {
            max-width: $reading-measure-wide;
            margin: 0 auto;
        }

        .placeholder .skeleton {
            height: 5.5rem;
            margin-bottom: 1rem;
        }

        .failure {
            color: var(--ui-error);
        }

        .blank {
            padding-top: 4rem;
            text-align: left;

            h1 {
                margin: 0 0 0.75rem;
                font-family: var(--font-prose);
                font-size: var(--nt-text-xl);
                font-weight: 400;
                color: var(--ui-text-highlighted);
            }

            p {
                max-width: 32rem;
                margin: 0 0 1.5rem;
                line-height: 1.6;
                color: var(--ui-text-muted);
            }
        }
    }
</style>
