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
                <span class="title">Models</span>

                <ModelField
                    v-model="form.defaultModel"
                    label="Default model"
                    description="Used by novels created from now on. Existing books keep theirs."
                />

                <ModelField
                    v-model="form.glossaryModel"
                    label="Glossary model"
                    description="Used for listing names and reading back how a term was rendered — not for prose."
                />

                <p class="note">
                    These are deliberately separate. Glossary work is mechanical and there is a great
                    deal of it; a book translated with an expensive model should not pay that price
                    forty times over per chapter just to list the names in a passage.
                </p>
            </section>

            <section class="group">
                <span class="title">Local model</span>

                <UFormField
                    label="Endpoint"
                    description="Any OpenAI-compatible server. Ollama's is http://localhost:11434/v1 — the /v1 matters. Leave empty to keep glossary work on the subscription."
                >
                    <UInput v-model="form.localModelEndpoint" placeholder="http://localhost:11434/v1" />
                </UFormField>

                <UFormField label="Model" description="What that server should load.">
                    <div class="field">
                        <UInputMenu
                            v-model="form.localModelName"
                            :items="localModelNames"
                            :loading="isLoadingLocal"
                            placeholder="qwen2.5:7b"
                            class="picker"
                        />

                        <UButton
                            color="neutral"
                            variant="subtle"
                            :loading="isLoadingLocal"
                            :disabled="form.localModelEndpoint.trim() === ''"
                            @click="loadLocalModels"
                        >
                            Refresh
                        </UButton>
                    </div>

                    <!-- Unlike the Claude side this really is a list: every OpenAI-compatible server
                         answers /v1/models with what it has. Free text stays possible because the
                         server has to be running for the list to exist. -->
                    <p v-if="localModels" class="note" :class="{ bad: !localModels.reachable }">
                        <template v-if="localModels.reachable">
                            {{ localModels.models.length }} model(s) loaded there.
                        </template>

                        <template v-else>
                            {{ localModels.error ? describe(localModels.error) : "That server did not answer." }}
                        </template>
                    </p>
                </UFormField>

                <UFormField
                    label="API key"
                    description="Local servers ignore it, but the client has to send something."
                >
                    <UInput v-model="form.localModelApiKey" placeholder="not-needed" />
                </UFormField>
            </section>

            <section class="group">
                <span class="title">Batching</span>

                <div class="pair">
                    <UFormField
                        label="Output ceiling, tokens"
                        description="The model's own limit. What truncates a reply, so the input budget is derived from it."
                    >
                        <UInputNumber v-model="form.maxOutputTokens" :min="2000" :max="128000" :step="1000" />
                    </UFormField>

                    <UFormField
                        label="Expansion factor"
                        description="How much larger the translation is expected to be than its source."
                    >
                        <UInputNumber v-model="form.expansionFactor" :min="0.5" :max="6" :step="0.1" />
                    </UFormField>
                </div>

                <p class="note">
                    2.0 is conservative because it has to hold for every language pair. It is worth
                    tuning: Latin into Cyrillic really does roughly double, but Japanese into Russian
                    barely grows, and leaving it at 2.0 there wastes half of every request.
                </p>
            </section>

            <section class="group">
                <span class="title">Voice window</span>

                <div class="pair">
                    <UFormField
                        label="Chapters sampled"
                        description="How many finished chapters are shown as an example of the established voice. Zero turns it off."
                    >
                        <UInputNumber v-model="form.voiceWindowChapters" :min="0" :max="10" />
                    </UFormField>

                    <UFormField label="Paragraphs from each" description="How much of each of them.">
                        <UInputNumber v-model="form.voiceWindowParagraphs" :min="0" :max="20" />
                    </UFormField>
                </div>

                <p class="note">
                    A straight trade of cost against consistency: this text is sent with every
                    request, and it is what keeps chapter fifty sounding like chapter one.
                </p>
            </section>

            <section class="group">
                <span class="title">Editing passes</span>

                <div class="pair">
                    <UFormField
                        label="Paragraphs per pass"
                        description="How many paragraphs are handed to the model at once, both translating and repairing."
                    >
                        <UInputNumber v-model="form.passSegments" :min="1" :max="50" />
                    </UFormField>

                    <UFormField
                        label="Thinking budget, tokens"
                        description="How much the model may think before answering each pass and each proofread. Zero turns thinking off."
                    >
                        <UInputNumber v-model="form.thinkingTokens" :min="0" :max="32000" :step="500" />
                    </UFormField>
                </div>

                <div class="pair">
                    <UFormField
                        label="Context before"
                        description="Paragraphs shown before the batch for the model to read — never translated, repaired or output."
                    >
                        <UInputNumber v-model="form.passContextBefore" :min="0" :max="20" />
                    </UFormField>

                    <UFormField
                        label="Context after"
                        description="Paragraphs shown after the batch, the same way."
                    >
                        <UInputNumber v-model="form.passContextAfter" :min="0" :max="20" />
                    </UFormField>
                </div>

                <USwitch
                    v-model="form.proofread"
                    label="Proofread each chapter"
                    description="Read the finished chapter back once more and redo whatever still reads wrong."
                />

                <NullableModelField
                    v-model="form.repairModel"
                    label="Repair model"
                    default-label="Book's own model"
                    description="Overrides the book's own model for repair runs only. Translate runs are unaffected."
                />

                <p class="note">
                    Small batches read closely instead of skimming a whole chapter — a paragraph at a
                    time, with a memo before and a proofread after, the way a careful editor works
                    rather than a machine that translates once and moves on.
                </p>
            </section>

            <section class="group">
                <span class="title">Limits</span>

                <div class="pair">
                    <UFormField
                        label="Page load timeout, ms"
                        description="How long to wait for a site before giving up on it."
                    >
                        <UInputNumber v-model="form.pageLoadTimeoutMs" :min="5000" :max="300000" :step="1000" />
                    </UFormField>

                    <UFormField
                        label="Chat rounds"
                        description="How many tool calls the agent may make before it must answer."
                    >
                        <UInputNumber v-model="form.chatMaxRounds" :min="1" :max="20" />
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

            <p class="footnote">
                Numbers out of range are corrected rather than refused, and the fields above show
                what was actually stored — so a value you typed may come back changed.
            </p>
        </div>
    </div>
</template>

<script setup lang="ts">
    import { computed, reactive, ref, watch } from "vue";
    import ModelField from "@/components/settings/ModelField.vue";
    import NullableModelField from "@/components/settings/NullableModelField.vue";
    import { useLocalModels, useSettings, useUpdateSettings } from "@/composables/useSettings";
    import { describe } from "@/utils/status";


    const { data } = useSettings();
    const { mutateAsync, isPending: isSaving } = useUpdateSettings();

    // Not fetched on arrival: it reaches a server that is usually not running, and a failed request
    // every time this screen opens is noise rather than information.
    const wantLocalModels = ref(false);
    const { data: localModels, isFetching: isLoadingLocal, refetch } = useLocalModels(wantLocalModels);

    const saved = ref(false);

    const form = reactive({
        globalStyleGuide: "",
        defaultModel: "",
        glossaryModel: "",
        localModelEndpoint: "",
        localModelName: "",
        localModelApiKey: "",
        maxOutputTokens: 16000,
        expansionFactor: 2,
        voiceWindowChapters: 2,
        voiceWindowParagraphs: 4,
        passSegments: 5,
        passContextBefore: 2,
        passContextAfter: 1,
        thinkingTokens: 6000,
        proofread: true,
        repairModel: null as string | null,
        pageLoadTimeoutMs: 45000,
        chatMaxRounds: 5,
    });

    const localModelNames = computed<string[]>(() => localModels.value?.models ?? []);


    // Runs on load and again after every save, because the response is the saved state: a number the
    // server clamped has to replace what the user typed, or they never learn it was corrected.
    watch(data, (loaded) => {
        if (loaded === undefined) {
            return;
        }

        form.globalStyleGuide = loaded.globalStyleGuide ?? "";
        form.defaultModel = loaded.defaultModel;
        form.glossaryModel = loaded.glossaryModel;
        form.localModelEndpoint = loaded.localModelEndpoint ?? "";
        form.localModelName = loaded.localModelName;
        form.localModelApiKey = loaded.localModelApiKey;
        form.maxOutputTokens = loaded.maxOutputTokens;
        form.expansionFactor = loaded.expansionFactor;
        form.voiceWindowChapters = loaded.voiceWindowChapters;
        form.voiceWindowParagraphs = loaded.voiceWindowParagraphs;
        form.passSegments = loaded.passSegments;
        form.passContextBefore = loaded.passContextBefore;
        form.passContextAfter = loaded.passContextAfter;
        form.thinkingTokens = loaded.thinkingTokens;
        form.proofread = loaded.proofread;
        form.repairModel = loaded.repairModel;
        form.pageLoadTimeoutMs = loaded.pageLoadTimeoutMs;
        form.chatMaxRounds = loaded.chatMaxRounds;
    }, { immediate: true });


    async function loadLocalModels(): Promise<void> {
        // The endpoint has to be saved before it can be asked — the server reads it from the
        // database rather than from this form.
        await mutateAsync({ localModelEndpoint: form.localModelEndpoint.trim() });

        wantLocalModels.value = true;
        await refetch();
    }


    async function save(): Promise<void> {
        // Empty strings rather than nulls: on this API null means "leave alone" and an empty string
        // is what clears a nullable field, so emptying the notes has to send "".
        await mutateAsync({
            globalStyleGuide: form.globalStyleGuide.trim(),
            defaultModel: form.defaultModel.trim(),
            glossaryModel: form.glossaryModel.trim(),
            localModelEndpoint: form.localModelEndpoint.trim(),
            localModelName: form.localModelName.trim(),
            localModelApiKey: form.localModelApiKey.trim(),
            maxOutputTokens: form.maxOutputTokens,
            expansionFactor: form.expansionFactor,
            voiceWindowChapters: form.voiceWindowChapters,
            voiceWindowParagraphs: form.voiceWindowParagraphs,
            passSegments: form.passSegments,
            passContextBefore: form.passContextBefore,
            passContextAfter: form.passContextAfter,
            thinkingTokens: form.thinkingTokens,
            proofread: form.proofread,
            repairModel: form.repairModel ?? "",
            pageLoadTimeoutMs: form.pageLoadTimeoutMs,
            chatMaxRounds: form.chatMaxRounds,
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

            .title {
                color: var(--ui-text-highlighted);
            }

            .pair {
                display: grid;
                grid-template-columns: 1fr 1fr;
                gap: 1rem;
            }

            .field {
                display: flex;
                align-items: center;
                gap: 0.5rem;

                .picker {
                    flex: 1;
                    min-width: 0;
                }
            }

            .note {
                margin: 0;
                font-size: var(--nt-text-sm);
                line-height: 1.6;
                color: var(--ui-text-muted);

                &.bad {
                    color: var(--ui-warning);
                }
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
                width: 100%;
                max-width: $reading-measure-wide;
                margin: 0 auto;

                .saved {
                    color: var(--ui-success);
                }

                .spacer {
                    flex: 1;
                }
            }

            .footnote {
                width: 100%;
                max-width: $reading-measure-wide;
                margin: 1rem auto 0;
                font-size: var(--nt-text-sm);
                line-height: 1.6;
                color: var(--ui-text-dimmed);
            }
        }
    }
</style>
