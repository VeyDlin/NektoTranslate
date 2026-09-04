<template>
    <div class="import-translation">
        <header class="bar">
            <UButton
                :to="{ name: 'novel', params: { novelId } }"
                icon="i-material-symbols:arrow-back-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                aria-label="Back to the chapter list"
            />

            <span class="book">{{ novel?.title ?? "" }}</span>

            <span class="where">Import an existing translation</span>

            <span class="spacer" />

            <UButton :to="{ name: 'alignment', params: { novelId } }" size="sm" color="neutral" variant="ghost">
                Alignment
            </UButton>

            <UColorModeButton size="sm" />
        </header>

        <div class="step">
            <UInput
                v-model="url"
                icon="i-material-symbols:link-rounded"
                placeholder="Address of the translation's contents page"
                size="sm"
                class="url"
                @keydown.enter="loadContents"
            />

            <UButton size="sm" :loading="isLoading" :disabled="url.trim() === ''" @click="loadContents">
                Read the contents
            </UButton>

            <span v-if="support && !support.supported" class="unsupported">
                No parser claims that address.
            </span>

            <span v-else-if="support?.parser" class="supported">
                Read by <strong>{{ support.parser }}</strong>
            </span>
        </div>

        <Transition name="pick">
            <div v-if="links.length > 0" class="picked">
                <span>{{ formatCount(selectedLinks.length) }} of {{ formatCount(links.length) }} chosen</span>

                <UButton size="xs" color="neutral" variant="ghost" @click="selectAll">
                    Select all
                </UButton>

                <UButton size="xs" color="neutral" variant="ghost" @click="rowSelection = {}">
                    Clear
                </UButton>

                <span class="gap" />

                <span class="starts">First chosen entry becomes chapter</span>

                <UInputNumber v-model="startAt" :min="0" :max="100000" size="sm" class="start" />

                <span class="spacer" />

                <UButton
                    size="sm"
                    :disabled="selectedLinks.length === 0"
                    :loading="isImporting"
                    @click="runImport"
                >
                    Import {{ formatCount(selectedLinks.length) }} into {{ language }}
                </UButton>
            </div>
        </Transition>

        <!-- The one thing this screen exists to get right. Translation sites routinely open with a
             translator's note, so entry one is not chapter one, and every later chapter inherits the
             mistake. Showing the mapping before the import makes that visible while it is still
             free to fix. -->
        <p v-if="selectedLinks.length > 0" class="mapping">
            <strong>{{ firstTitle }}</strong> → chapter {{ startAt }}
            <template v-if="selectedLinks.length > 1">
                &nbsp;·&nbsp; <strong>{{ lastTitle }}</strong> → chapter {{ startAt + selectedLinks.length - 1 }}
            </template>
        </p>

        <UTable
            v-if="links.length > 0"
            v-model:row-selection="rowSelection"
            :data="links"
            :columns="columns"
            :get-row-id="getRowId"
            :virtualize="{ estimateSize: 40, overscan: 14 }"
            sticky
            class="flex-1 min-h-0"
            :ui="{ th: 'py-2 text-xs font-normal text-dimmed', td: 'py-0 h-10' }"
        />

        <div v-else class="blank">
            <h1>Continue someone else's translation</h1>
            <p>
                Paste the contents page of a translation this book already has. It is stored beside
                the original exactly as our own output would be, so the glossary can read back what
                the previous translator called each character — which is what keeps chapter twenty-one
                using the names of chapters one to twenty.
            </p>
            <p>
                Nothing is ever overwritten. A chapter that already has a
                {{ language }} translation is reported and skipped.
            </p>
        </div>

        <Transition name="pick">
            <div v-if="result" class="result" :class="{ partial: result.rejected.length > 0 }">
                <p class="headline">
                    Imported {{ formatCount(result.imported) }}.
                    <template v-if="result.rejected.length > 0">
                        {{ formatCount(result.rejected.length) }} did not land.
                    </template>
                </p>

                <ul v-if="result.rejected.length > 0" class="failures">
                    <li v-for="rejection in result.rejected" :key="rejection.chapterIndex">
                        Chapter {{ rejection.chapterIndex }}: {{ describe(rejection.reason) }}
                    </li>
                </ul>

                <UButton :to="{ name: 'alignment', params: { novelId } }" size="sm">
                    Check the alignment
                </UButton>
            </div>
        </Transition>
    </div>
</template>

<script setup lang="ts">
    import type { TableColumn } from "@nuxt/ui";
    import type { ParserSupport, TranslationImportResult } from "@/types/api/requests";
    import type { ParsedChapterLink } from "@/types/models/domain";

    import { useQueryClient } from "@tanstack/vue-query";
    import { computed, h, ref, resolveComponent } from "vue";
    import { parsingApi } from "@/api";
    import { useNovel } from "@/composables/useNovels";
    import { formatCount } from "@/utils/format";
    import { describe } from "@/utils/status";


    const props = defineProps<{ novelId: string }>();

    const queryClient = useQueryClient();
    const id = computed(() => Number(props.novelId));

    const { data: novelData } = useNovel(id);

    const novel = computed(() => novelData.value ?? null);
    const language = computed(() => novel.value?.targetLanguage ?? "");

    const url = ref("");
    const support = ref<ParserSupport | null>(null);
    const links = ref<ParsedChapterLink[]>([]);
    const rowSelection = ref<Record<string, boolean>>({});
    const result = ref<TranslationImportResult | null>(null);
    const startAt = ref(0);
    const isLoading = ref(false);
    const isImporting = ref(false);

    const UCheckbox = resolveComponent("UCheckbox");

    const columns: TableColumn<ParsedChapterLink>[] = [
        {
            id: "select",
            header: ({ table }) => h(UCheckbox, {
                "modelValue": table.getIsSomeRowsSelected() ? "indeterminate" : table.getIsAllRowsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => table.toggleAllRowsSelected(!!value),
                "aria-label": "Select every entry",
            }),
            cell: ({ row }) => h(UCheckbox, {
                "modelValue": row.getIsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => row.toggleSelected(!!value),
                "aria-label": `Select ${row.original.title}`,
            }),
            meta: { class: { th: "w-8", td: "w-8" } },
        },
        {
            accessorKey: "title",
            header: "Entry",
        },
    ];

    // Order matters and is the site's, not the click order: entries are mapped to consecutive
    // chapters in the order they appear in the contents.
    const selectedLinks = computed(() => links.value.filter(link => rowSelection.value[link.sourceUrl] === true));

    const firstTitle = computed(() => selectedLinks.value[0]?.title ?? "");
    const lastTitle = computed(() => selectedLinks.value[selectedLinks.value.length - 1]?.title ?? "");


    function getRowId(link: ParsedChapterLink): string {
        return link.sourceUrl;
    }


    function selectAll(): void {
        rowSelection.value = Object.fromEntries(links.value.map(link => [link.sourceUrl, true]));
    }


    // Support is checked first because it is answered from the host name alone. Telling the user the
    // address is unreadable costs nothing; finding out after a download does.
    async function loadContents(): Promise<void> {
        const address = url.value.trim();

        if (address === "") {
            return;
        }

        isLoading.value = true;
        result.value = null;

        try {
            support.value = await parsingApi.support(address);

            if (!support.value.supported) {
                links.value = [];
                return;
            }

            links.value = await parsingApi.tableOfContents(address);
            rowSelection.value = {};
        }
        finally {
            isLoading.value = false;
        }
    }


    async function runImport(): Promise<void> {
        isImporting.value = true;

        try {
            result.value = await parsingApi.importTranslation(
                id.value,
                language.value,
                selectedLinks.value,
                startAt.value,
            );

            rowSelection.value = {};
            void queryClient.invalidateQueries({ queryKey: ["novels", id.value] });
        }
        finally {
            isImporting.value = false;
        }
    }
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .import-translation {
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

        .step {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.75rem;
            padding: 0.75rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);

            .url {
                flex: 1;
                min-width: 0;
            }

            .unsupported {
                flex: none;
                color: var(--ui-warning);
            }

            .supported {
                flex: none;
                color: var(--ui-text-muted);
            }
        }

        .picked {
            flex: none;
            display: flex;
            align-items: center;
            gap: 0.75rem;
            padding: 0.5rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);
            background: var(--ui-bg-elevated);

            .gap {
                width: 1rem;
            }

            .starts {
                color: var(--ui-text-muted);
                font-size: var(--nt-text-sm);
            }

            .start {
                width: 7rem;
            }

            .spacer {
                flex: 1;
            }
        }

        .mapping {
            flex: none;
            margin: 0;
            padding: 0.625rem 1.5rem;
            border-bottom: 1px solid var(--ui-border);
            font-size: var(--nt-text-sm);
            color: var(--ui-text-muted);

            strong {
                color: var(--ui-text-highlighted);
                font-weight: 400;
            }
        }

        .blank {
            max-width: $reading-measure-comfortable;
            margin: 0 auto;
            padding: 5rem 1.5rem;

            h1 {
                margin: 0 0 0.75rem;
                font-family: var(--font-prose);
                font-size: var(--nt-text-xl);
                font-weight: 400;
                color: var(--ui-text-highlighted);
            }

            p {
                margin: 0 0 0.75rem;
                line-height: 1.6;
                color: var(--ui-text-muted);
            }
        }

        .result {
            flex: none;
            padding: 1rem 1.5rem;
            border-top: 1px solid var(--ui-border);
            box-shadow: inset 2px 0 0 var(--ui-success);

            &.partial {
                box-shadow: inset 2px 0 0 var(--ui-warning);
            }

            .headline {
                margin: 0 0 0.5rem;
                color: var(--ui-text-highlighted);
            }

            .failures {
                max-width: $reading-measure-comfortable;
                margin: 0 0 0.75rem;
                padding-left: 1.25rem;
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);
            }
        }
    }

    .pick-enter-active,
    .pick-leave-active {
        transition: opacity 0.15s ease-out;
    }

    .pick-enter-from,
    .pick-leave-to {
        opacity: 0;
    }
</style>
