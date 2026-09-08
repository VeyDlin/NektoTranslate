<template>
    <div class="novel-settings">
        <AppBar>
            <UButton
                :to="{ name: 'novel', params: { novelId } }"
                icon="i-material-symbols:arrow-back-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                aria-label="Back to the chapter list"
            />

            <span
                class="book"
                data-bar-text
                :lang="scriptLangIf(novel?.title ?? '', scriptLang)"
            >{{ novel?.title ?? "" }}</span>

            <span class="where" data-bar-text>Settings</span>

            <span class="spacer" data-bar-text />

            <UColorModeButton size="sm" />
        </AppBar>

        <div class="sheet">
            <section class="group">
                <UFormField label="Title">
                    <UInput v-model="form.title" :lang="scriptLangIf(form.title, scriptLang)" />
                </UFormField>

                <div class="pair">
                    <UFormField label="Written in">
                        <UInput v-model="form.sourceLanguage" />
                    </UFormField>

                    <UFormField label="Translate into">
                        <UInput v-model="form.targetLanguage" />
                    </UFormField>
                </div>

                <p class="note">
                    Languages are free text. The model reads them as names, so an unusual pair works
                    as well as a common one.
                </p>

                <UFormField label="Where it came from" hint="Optional">
                    <UInput v-model="form.sourceUrl" placeholder="https://" />
                </UFormField>
            </section>

            <section class="group">
                <UFormField
                    label="Style notes for this book"
                    description="Tone, register, how to handle honorifics. Sent with every chapter of this novel."
                >
                    <UTextarea v-model="form.styleGuide" :rows="5" />
                </UFormField>

                <p class="note">
                    The application-wide notes apply first; these come after and win where the two
                    disagree. Neither can replace the built-in instructions that carry the segment
                    protocol.
                    <ULink :to="{ name: 'settings' }" class="link">Application settings</ULink>
                </p>
            </section>

            <section class="group">
                <ModelField
                    v-model="form.model"
                    label="Model"
                    description="Which model translates this book. Changing it makes the next run genuinely re-translate."
                />

                <USwitch
                    v-model="form.normalizeQuotes"
                    label="Convert quotation marks to the target language's convention"
                />
            </section>

            <div class="actions">
                <span v-if="saved" class="saved">Saved</span>

                <span class="spacer" />

                <UButton :to="{ name: 'novel', params: { novelId } }" color="neutral" variant="ghost">
                    Cancel
                </UButton>

                <UButton :loading="isSaving" @click="save">
                    Save changes
                </UButton>
            </div>

            <section class="danger">
                <div class="text">
                    <span class="heading">Delete this novel</span>
                    <p>
                        Its chapters, translations, glossary, chat and run history go with it. The
                        source files on whatever site it came from are untouched.
                    </p>
                </div>

                <UButton color="error" variant="subtle" @click="asking = true">
                    Delete
                </UButton>
            </section>
        </div>

        <UModal
            v-model:open="asking"
            :title="`Delete ${novel?.title ?? 'this novel'}?`"
            description="Everything about the book is removed: chapters, translations, glossary, chat and run history."
        >
            <template #footer>
                <div class="confirm">
                    <UButton color="neutral" variant="ghost" @click="asking = false">
                        Keep it
                    </UButton>

                    <UButton color="error" :loading="isDeleting" @click="confirmDelete">
                        Delete the novel
                    </UButton>
                </div>
            </template>
        </UModal>
    </div>
</template>

<script setup lang="ts">
    import { computed, reactive, ref, watch } from "vue";
    import { useRouter } from "vue-router";
    import AppBar from "@/components/common/AppBar.vue";
    import ModelField from "@/components/settings/ModelField.vue";
    import { useDeleteNovel, useNovel, useUpdateNovel } from "@/composables/useNovels";
    import { scriptLangFor, scriptLangIf } from "@/utils/language";


    const props = defineProps<{ novelId: string }>();

    const router = useRouter();
    const id = computed(() => Number(props.novelId));

    const { data: novelData } = useNovel(id);
    const { mutateAsync: updateNovel, isPending: isSaving } = useUpdateNovel(id);
    const { mutateAsync: deleteNovel, isPending: isDeleting } = useDeleteNovel();

    const novel = computed(() => novelData.value ?? null);
    const scriptLang = computed(() => (novel.value === null ? undefined : scriptLangFor(novel.value.sourceLanguage)));

    const asking = ref(false);
    const saved = ref(false);

    const form = reactive({
        title: "",
        sourceLanguage: "",
        targetLanguage: "",
        sourceUrl: "",
        styleGuide: "",
        model: "",
        normalizeQuotes: true,
    });


    // Filled from the server rather than kept as the source of truth, so opening the screen always
    // shows what is stored instead of whatever was typed and abandoned last time.
    watch(novel, (loaded) => {
        if (loaded === null) {
            return;
        }

        form.title = loaded.title;
        form.sourceLanguage = loaded.sourceLanguage;
        form.targetLanguage = loaded.targetLanguage;
        form.sourceUrl = loaded.sourceUrl ?? "";
        form.styleGuide = loaded.styleGuide ?? "";
        form.model = loaded.model;
        form.normalizeQuotes = loaded.normalizeQuotes;
    }, { immediate: true });


    async function save(): Promise<void> {
        // Empty strings are sent, not swallowed: on this API an empty string clears a nullable field
        // while null means "leave alone", so clearing the style notes has to send "".
        await updateNovel({
            title: form.title.trim(),
            sourceLanguage: form.sourceLanguage.trim(),
            targetLanguage: form.targetLanguage.trim(),
            sourceUrl: form.sourceUrl.trim(),
            styleGuide: form.styleGuide.trim(),
            model: form.model.trim(),
            normalizeQuotes: form.normalizeQuotes,
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


    async function confirmDelete(): Promise<void> {
        await deleteNovel(id.value);
        asking.value = false;
        await router.push({ name: "library" });
    }
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .novel-settings {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;

        .bar {
            .book {
                color: var(--ui-text-muted);
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }

            .where {
                flex: none;
                color: var(--ui-text-highlighted);
            }

            .spacer {
                flex: 1;
            }
        }

        // The scroll region itself spans the full window width, edge to edge, matching LibraryView's
        // `.page` — only the content inside it is held to a readable measure, so the scrollbar never
        // sits stranded in the middle of the screen with empty gutters on both sides.
        .sheet {
            flex: 1;
            min-height: 0;
            overflow-y: auto;
            width: 100%;
            padding: 2.5rem 1.5rem 4rem;

            .group {
                display: flex;
                flex-direction: column;
                gap: 1rem;
                width: 100%;
                max-width: $reading-measure-wide;
                margin: 0 auto 2rem;
                padding-bottom: 2rem;
                border-bottom: 1px solid var(--ui-border);
            }

            .pair {
                display: grid;
                grid-template-columns: 1fr 1fr;
                gap: 1rem;
            }

            .narrow {
                max-width: 14rem;
            }

            .note {
                margin: -0.25rem 0 0;
                font-size: var(--nt-text-sm);
                line-height: 1.6;
                color: var(--ui-text-muted);

                .link {
                    color: var(--ui-primary);
                }
            }

            .actions {
                display: flex;
                align-items: center;
                gap: 0.5rem;
                width: 100%;
                max-width: $reading-measure-wide;
                margin: 0 auto 3rem;

                .saved {
                    color: var(--ui-success);
                }

                .spacer {
                    flex: 1;
                }
            }

            // Set apart rather than dropped in with the rest: it is the one control on this screen
            // whose effect cannot be undone by pressing Cancel.
            .danger {
                display: flex;
                align-items: flex-start;
                gap: 1.5rem;
                width: 100%;
                max-width: $reading-measure-wide;
                margin: 0 auto;
                padding: 1.25rem;
                box-shadow: inset 2px 0 0 var(--ui-error);
                background: var(--ui-bg-muted);

                .text {
                    flex: 1;
                }

                .heading {
                    color: var(--ui-text-highlighted);
                }

                p {
                    margin: 0.375rem 0 0;
                    font-size: var(--nt-text-sm);
                    line-height: 1.6;
                    color: var(--ui-text-muted);
                }
            }
        }
    }

    .confirm {
        display: flex;
        justify-content: flex-end;
        gap: 0.5rem;
        width: 100%;
    }
</style>
