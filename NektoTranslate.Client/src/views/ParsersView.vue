<template>
    <div class="parsers">
        <header class="bar">
            <UButton
                :to="{ name: 'library' }"
                icon="i-material-symbols:arrow-back-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                aria-label="Back to the library"
            />

            <span class="where">Supported sites</span>

            <span class="spacer" />

            <span class="counts">{{ formatCount(parsers.length) }} listed</span>

            <UColorModeButton size="sm" />
        </header>

        <div class="panes">
            <aside class="sites">
                <ul class="list">
                    <li
                        v-for="parser in parsers"
                        :key="parser.hostName"
                        class="site"
                        :class="{ chosen: parser.hostName === selected, off: !parser.enabled }"
                    >
                        <button type="button" class="pick" @click="select(parser.hostName)">
                            <span class="host">{{ parser.hostName }}</span>

                            <span class="tags">
                                <UBadge v-if="!parser.bundled" size="sm" variant="subtle" color="neutral">
                                    Added
                                </UBadge>

                                <UBadge v-if="parser.edited && parser.bundled" size="sm" variant="subtle" color="warning">
                                    Edited
                                </UBadge>

                                <UBadge v-if="!parser.enabled" size="sm" variant="subtle" color="neutral">
                                    Off
                                </UBadge>
                            </span>
                        </button>
                    </li>
                </ul>
            </aside>

            <section v-if="selected" class="script">
                <div class="tools">
                    <span class="host" :title="selected">{{ selected }}</span>

                    <USwitch
                        v-model="enabled"
                        size="sm"
                        :label="enabled ? 'On' : 'Off'"
                    />

                    <span class="spacer" />

                    <UButton
                        v-if="canRevert"
                        size="sm"
                        color="neutral"
                        variant="ghost"
                        :loading="isReverting"
                        @click="revert"
                    >
                        {{ chosen?.bundled ? "Revert to bundled" : "Remove" }}
                    </UButton>

                    <UButton size="sm" :loading="isSaving" @click="save">
                        Save
                    </UButton>
                </div>

                <textarea
                    v-model="source"
                    class="code"
                    spellcheck="false"
                    aria-label="Parser script"
                />

                <p class="note">
                    Bundled parsers stay as files on disk. Saving here stores an override that shadows
                    the bundled one, which is why reverting brings the original back rather than
                    needing a reinstall.
                </p>
            </section>

            <section v-else class="blank">
                <h1>Pick a site</h1>
                <p>Each entry is a small script that finds a novel's chapter list and a chapter's text.</p>
            </section>
        </div>
    </div>
</template>

<script setup lang="ts">
    import { useMutation, useQuery, useQueryClient } from "@tanstack/vue-query";
    import { computed, ref, watch } from "vue";
    import { parsersApi } from "@/api";
    import { formatCount } from "@/utils/format";


    const queryClient = useQueryClient();

    const selected = ref<string | null>(null);
    const source = ref("");
    const enabled = ref(true);

    const { data: parserData } = useQuery({
        queryKey: ["parsers"],
        queryFn: () => parsersApi.list(),
    });

    const parsers = computed(() => parserData.value ?? []);
    const chosen = computed(() => parsers.value.find(parser => parser.hostName === selected.value) ?? null);

    // Only an override can be reverted. A bundled parser nobody has touched has nothing to undo.
    const canRevert = computed(() => chosen.value?.edited === true || chosen.value?.bundled === false);

    const { data: sourceData } = useQuery({
        queryKey: computed(() => ["parsers", selected.value, "source"]),
        queryFn: () => parsersApi.source(selected.value as string),
        enabled: computed(() => selected.value !== null),
    });

    const { mutateAsync: saveScript, isPending: isSaving } = useMutation({
        mutationFn: () => parsersApi.upsert(selected.value as string, {
            scriptSource: source.value,
            enabled: enabled.value,
        }),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ["parsers"] });
        },
    });

    const { mutateAsync: revertScript, isPending: isReverting } = useMutation({
        mutationFn: () => parsersApi.revert(selected.value as string),
        onSuccess: () => {
            void queryClient.invalidateQueries({ queryKey: ["parsers"] });
        },
    });


    watch(sourceData, (loaded) => {
        source.value = loaded ?? "";
    });


    function select(hostName: string): void {
        selected.value = hostName;
        enabled.value = parsers.value.find(parser => parser.hostName === hostName)?.enabled ?? true;
    }


    async function save(): Promise<void> {
        await saveScript();
    }


    async function revert(): Promise<void> {
        const host = selected.value;

        await revertScript();

        // A site the user added is gone after reverting; a bundled one is still there with its
        // original script, so the pane stays open on it.
        const stillListed = parsers.value.some(parser => parser.hostName === host && parser.bundled);

        if (!stillListed) {
            selected.value = null;
            source.value = "";
        }
        else {
            void queryClient.invalidateQueries({ queryKey: ["parsers", host, "source"] });
        }
    }
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .parsers {
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

            .counts {
                color: var(--ui-text-muted);
            }

            .spacer {
                flex: 1;
            }
        }

        .panes {
            flex: 1;
            min-height: 0;
            display: grid;
            grid-template-columns: 20rem 1fr;
        }

        .sites {
            overflow-y: auto;
            border-right: 1px solid var(--ui-border);

            .list {
                margin: 0;
                padding: 0;
                list-style: none;
            }

            .site {
                border-bottom: 1px solid var(--ui-border);

                &.chosen {
                    background: var(--ui-bg-elevated);
                    box-shadow: inset 2px 0 0 var(--ui-primary);
                }

                &.off .host {
                    color: var(--ui-text-dimmed);
                }

                .pick {
                    display: flex;
                    align-items: center;
                    gap: 0.5rem;
                    width: 100%;
                    padding: 0.625rem 1rem;
                    border: 0;
                    background: transparent;
                    text-align: left;
                    color: inherit;
                    font: inherit;

                    &:hover {
                        background: var(--ui-bg-elevated);
                    }
                }

                .host {
                    flex: 1;
                    min-width: 0;
                    overflow: hidden;
                    text-overflow: ellipsis;
                    white-space: nowrap;
                }

                .tags {
                    display: flex;
                    gap: 0.25rem;
                }
            }
        }

        .script {
            display: flex;
            flex-direction: column;
            min-height: 0;

            .tools {
                flex: none;
                display: flex;
                align-items: center;
                gap: 0.75rem;
                padding: 0.75rem 1.5rem;
                border-bottom: 1px solid var(--ui-border);

                .host {
                    color: var(--ui-text-highlighted);
                }

                .spacer {
                    flex: 1;
                }
            }

            // A parser is JavaScript, so it is set in a monospace face and left alone. A rich text
            // editor here would fight the content rather than help with it.
            .code {
                flex: 1;
                min-height: 0;
                width: 100%;
                padding: 1.25rem 1.5rem;
                border: 0;
                resize: none;
                background: var(--ui-bg-muted);
                color: var(--ui-text);
                font-family: ui-monospace, "Cascadia Mono", "Consolas", monospace;
                font-size: var(--nt-text-base);
                line-height: 1.6;
                tab-size: 4;

                &:focus-visible {
                    outline: 2px solid var(--ui-primary);
                    outline-offset: -2px;
                }
            }

            .note {
                flex: none;
                max-width: $reading-measure-wide;
                margin: 0;
                padding: 0.75rem 1.5rem;
                border-top: 1px solid var(--ui-border);
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);
            }
        }

        .blank {
            padding: 5rem 2rem;

            h1 {
                margin: 0 0 0.75rem;
                font-family: var(--font-prose);
                font-size: var(--nt-text-xl);
                font-weight: 400;
                color: var(--ui-text-highlighted);
            }

            p {
                max-width: 32rem;
                margin: 0;
                line-height: 1.6;
                color: var(--ui-text-muted);
            }
        }
    }
</style>
