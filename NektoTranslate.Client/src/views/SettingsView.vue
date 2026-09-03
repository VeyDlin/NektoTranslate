<template>
    <div class="settings-page">
        <header class="bar">
            <UButton
                :to="{ name: 'library' }"
                icon="i-material-symbols:arrow-back-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                aria-label="Back to the library"
            />

            <span class="where">Application settings</span>

            <span class="spacer" />

            <UButton :to="{ name: 'parsers' }" size="sm" color="neutral" variant="ghost">
                Supported sites
            </UButton>

            <UColorModeButton size="sm" />
        </header>

        <div class="sheet">
            <section class="group">
                <UFormField
                    label="Style notes for every book"
                    description="Applied to every novel, before that novel's own notes."
                >
                    <UTextarea v-model="form.globalStyleGuide" :rows="5" />
                </UFormField>

                <!-- The brief asks for this to be spelled out, and rightly: three sets of
                     instructions stack here and the order decides which one wins. -->
                <div class="layers">
                    <span class="heading">How the instructions stack</span>

                    <ol class="stack">
                        <li>
                            <span class="rank">First</span>
                            <span class="what">These notes, on every book.</span>
                        </li>
                        <li>
                            <span class="rank">Then</span>
                            <span class="what">
                                The book's own notes, which win wherever the two disagree.
                            </span>
                        </li>
                        <li>
                            <span class="rank">Always</span>
                            <span class="what">
                                The built-in instructions that carry the segment protocol. Neither of
                                the above can replace them.
                            </span>
                        </li>
                    </ol>
                </div>
            </section>

            <section class="group">
                <div class="pair">
                    <UFormField
                        label="Default model"
                        description="Used by novels created from now on. Existing books keep theirs."
                    >
                        <UInput v-model="form.defaultModel" placeholder="sonnet" />
                    </UFormField>

                    <UFormField
                        label="Glossary model"
                        description="Used for extracting and settling terms rather than for prose."
                    >
                        <UInput v-model="form.glossaryModel" placeholder="sonnet" />
                    </UFormField>
                </div>
            </section>

            <div class="actions">
                <span v-if="saved" class="saved">Saved</span>

                <span class="spacer" />

                <UButton :loading="isSaving" @click="save">
                    Save changes
                </UButton>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
    import { reactive, ref, watch } from "vue";
    import { useSettings, useUpdateSettings } from "@/composables/useSettings";


    const { data } = useSettings();
    const { mutateAsync, isPending: isSaving } = useUpdateSettings();

    const saved = ref(false);

    const form = reactive({
        globalStyleGuide: "",
        defaultModel: "",
        glossaryModel: "",
    });


    watch(data, (loaded) => {
        if (loaded === undefined) {
            return;
        }

        form.globalStyleGuide = loaded.globalStyleGuide ?? "";
        form.defaultModel = loaded.defaultModel;
        form.glossaryModel = loaded.glossaryModel;
    }, { immediate: true });


    async function save(): Promise<void> {
        // Empty strings rather than nulls: on this API null means "leave alone" and an empty string
        // is what clears a nullable field, so emptying the notes has to send "".
        await mutateAsync({
            globalStyleGuide: form.globalStyleGuide.trim(),
            defaultModel: form.defaultModel.trim(),
            glossaryModel: form.glossaryModel.trim(),
        });

        flashSaved();
    }


    // A brief acknowledgement rather than a toast: the change is on this screen, so the confirmation
    // belongs next to the button that made it.
    function flashSaved(): void {
        saved.value = true;

        window.setTimeout(() => {
            saved.value = false;
        }, 2500);
    }
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .settings-page {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;

        .bar {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.875rem;
            height: $chrome-height;
            padding: 0 1rem 0 0.5rem;
            border-bottom: 1px solid var(--ui-border);

            .where {
                color: var(--ui-text-highlighted);
            }

            .spacer {
                flex: 1;
            }
        }

        .sheet {
            flex: 1;
            min-height: 0;
            overflow-y: auto;
            width: 100%;
            max-width: $reading-measure-wide;
            margin: 0 auto;
            padding: 2.5rem 1.5rem 4rem;

            .group {
                display: flex;
                flex-direction: column;
                gap: 1rem;
                padding-bottom: 2rem;
                margin-bottom: 2rem;
                border-bottom: 1px solid var(--ui-border);
            }

            .pair {
                display: grid;
                grid-template-columns: 1fr 1fr;
                gap: 1rem;
            }

            .layers {
                padding: 1rem 1.25rem;
                background: var(--ui-bg-muted);

                .heading {
                    color: var(--ui-text-highlighted);
                }

                // Numbered because the content genuinely is a sequence: which instruction wins
                // depends on the order they are applied in.
                .stack {
                    margin: 0.75rem 0 0;
                    padding: 0;
                    list-style: none;
                    display: flex;
                    flex-direction: column;
                    gap: 0.5rem;

                    li {
                        display: grid;
                        grid-template-columns: 4.5rem 1fr;
                        gap: 0.75rem;
                        font-size: var(--nt-text-sm);
                        line-height: 1.6;
                    }

                    .rank {
                        color: var(--ui-text-dimmed);
                    }

                    .what {
                        color: var(--ui-text-muted);
                    }
                }
            }

            .actions {
                display: flex;
                align-items: center;
                gap: 0.5rem;

                .saved {
                    color: var(--ui-success);
                }

                .spacer {
                    flex: 1;
                }
            }
        }
    }
</style>
