<template>
    <div class="import-url">
        <header class="bar">
            <UButton
                :to="{ name: 'novel', params: { novelId } }"
                icon="i-material-symbols:arrow-back-rounded"
                color="neutral"
                variant="ghost"
                size="sm"
                aria-label="Back to the chapter list"
            />

            <span class="book" :lang="scriptLangIf(novel?.title ?? '', scriptLang)">{{ novel?.title ?? "" }}</span>

            <span class="where">Import from a site</span>

            <span class="spacer" />

            <UButton :to="{ name: 'parsers' }" size="sm" color="neutral" variant="ghost">
                Supported sites
            </UButton>

            <UColorModeButton size="sm" />
        </header>

        <div class="step">
            <UInput
                v-model="url"
                icon="i-material-symbols:link-rounded"
                placeholder="Address of the novel's contents page"
                size="sm"
                class="url"
                @keydown.enter="loadContents"
            />

            <UButton size="sm" :loading="isLoading" :disabled="url.trim() === ''" @click="loadContents">
                Read the contents
            </UButton>

            <span v-if="support && !support.supported" class="unsupported">
                No parser claims that address. Its site may still be readable — check the list, or
                write a parser for it.
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

                <span class="spacer" />

                <UButton
                    size="sm"
                    :disabled="selectedLinks.length === 0"
                    :loading="isImporting"
                    @click="runImport"
                >
                    Import {{ formatCount(selectedLinks.length) }}
                </UButton>
            </div>
        </Transition>

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
        >
            <template #title-cell="{ row }">
                <span :lang="scriptLangIf(row.original.title, scriptLang)">{{ row.original.title }}</span>
            </template>
        </UTable>

        <div v-else class="blank">
            <h1>Nothing read yet</h1>
            <p>
                Paste the address of a novel's contents page. The chapter list is fetched first so you
                can choose what to bring in — a book with two thousand entries is never pulled down
                whole because a link was pasted.
            </p>
        </div>

        <Transition name="pick">
            <div v-if="result" class="result" :class="{ partial: result.failures.length > 0 }">
                <p class="headline">
                    Imported {{ formatCount(result.imported) }}
                    {{ result.imported === 1 ? "chapter" : "chapters" }}.
                    <template v-if="result.failures.length > 0">
                        {{ formatCount(result.failures.length) }} could not be read.
                    </template>
                </p>

                <ul v-if="result.failures.length > 0" class="failures">
                    <li v-for="failure in result.failures" :key="failure">{{ failure }}</li>
                </ul>

                <UButton :to="{ name: 'novel', params: { novelId } }" size="sm">
                    Back to the chapters
                </UButton>
            </div>
        </Transition>
    </div>
</template>

<script setup lang="ts">
    import type { TableColumn } from "@nuxt/ui";
    import type { ImportFromUrlResult, ParserSupport } from "@/types/api/requests";
    import type { ParsedChapterLink } from "@/types/models/domain";

    import { useQueryClient } from "@tanstack/vue-query";
    import { computed, h, ref, resolveComponent } from "vue";
    import { parsingApi } from "@/api";
    import { chaptersKey } from "@/composables/useChapters";
    import { useNovel } from "@/composables/useNovels";
    import { formatCount } from "@/utils/format";
    import { scriptLangFor, scriptLangIf } from "@/utils/language";


    const props = defineProps<{ novelId: string }>();

    const queryClient = useQueryClient();
    const id = computed(() => Number(props.novelId));

    const { data: novelData } = useNovel(id);

    const novel = computed(() => novelData.value ?? null);
    const scriptLang = computed(() => (novel.value === null ? undefined : scriptLangFor(novel.value.sourceLanguage)));

    const url = ref("");
    const support = ref<ParserSupport | null>(null);
    const links = ref<ParsedChapterLink[]>([]);
    const rowSelection = ref<Record<string, boolean>>({});
    const result = ref<ImportFromUrlResult | null>(null);
    const isLoading = ref(false);
    const isImporting = ref(false);

    const UCheckbox = resolveComponent("UCheckbox");

    const columns: TableColumn<ParsedChapterLink>[] = [
        {
            id: "select",
            header: ({ table }) => h(UCheckbox, {
                "modelValue": table.getIsSomeRowsSelected() ? "indeterminate" : table.getIsAllRowsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => table.toggleAllRowsSelected(!!value),
                "aria-label": "Select every chapter",
            }),
            cell: ({ row }) => h(UCheckbox, {
                "modelValue": row.getIsSelected(),
                "onUpdate:modelValue": (value: boolean | "indeterminate") => row.toggleSelected(!!value),
                "aria-label": `Select ${row.original.title}`,
            }),
            meta: { class: { th: "w-8", td: "w-8" } },
        },
        {
            id: "title",
            header: "Chapter",
        },
    ];

    const selectedLinks = computed(() => links.value.filter(link => rowSelection.value[link.sourceUrl] === true));


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
            result.value = await parsingApi.import(id.value, selectedLinks.value);
            rowSelection.value = {};
            void queryClient.invalidateQueries({ queryKey: chaptersKey(id.value) });
        }
        finally {
            isImporting.value = false;
        }
    }
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .import-url {
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
                max-width: 30rem;
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

            .spacer {
                flex: 1;
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
                margin: 0;
                line-height: 1.6;
                color: var(--ui-text-muted);
            }
        }

        // A batch that fetched most of what was asked for is a success with a note. The count leads;
        // the failures are listed underneath rather than replacing it.
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
