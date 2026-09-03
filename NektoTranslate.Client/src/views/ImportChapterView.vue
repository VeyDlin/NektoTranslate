<template>
    <div class="import">
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

            <span class="where">Add a chapter</span>

            <span class="spacer" />

            <span v-if="counts.characters > 0" class="counts">
                {{ formatCount(counts.paragraphs) }}
                {{ counts.paragraphs === 1 ? "paragraph" : "paragraphs" }},
                {{ formatCount(counts.characters) }} characters
            </span>

            <UColorModeButton size="sm" />
        </header>

        <div class="sheet">
            <label class="field">
                <span class="visually-hidden">Chapter title</span>

                <input
                    v-model="draft.title"
                    class="title"
                    type="text"
                    placeholder="Chapter title"
                    :lang="scriptLang"
                    :aria-invalid="problemField === 'title'"
                    autofocus
                    @input="problemField === 'title' && clearProblem()"
                >
            </label>

            <p v-if="problemField === 'title'" class="problem" role="alert">{{ problem }}</p>

            <UEditor
                v-model="draft.body"
                content-type="html"
                class="editor"
                placeholder="Paste the chapter here, or type / for commands. Formatting is kept."
                :starter-kit="{ codeBlock: false, code: false }"
                :ui="{ content: 'chapter-editor-content' }"
                @update:model-value="problemField === 'body' && clearProblem()"
            >
                <template #default="{ editor }">
                    <UEditorToolbar :editor="editor" :items="toolbar" class="toolbar" />

                    <UEditorDragHandle :editor="editor" />

                    <UEditorSuggestionMenu :editor="editor" :items="commands" />
                </template>
            </UEditor>

            <p v-if="problemField === 'body'" class="problem" role="alert">{{ problem }}</p>
        </div>

        <footer class="foot">
            <p class="note">
                Navigation, adverts, classes and inline styles are stripped on arrival. Paragraphs,
                headings, lists, emphasis, links, images and ruby are kept.
            </p>

            <span class="spacer" />

            <span v-if="hasWork" class="kept">
                <UIcon name="i-material-symbols:cloud-done-outline-rounded" class="kept-icon" />
                Draft kept on this machine
            </span>

            <UButton v-if="hasWork" color="neutral" variant="ghost" @click="discard">
                Discard
            </UButton>

            <UButton :to="{ name: 'novel', params: { novelId } }" color="neutral" variant="ghost">
                Cancel
            </UButton>

            <UButton :loading="isPending" @click="submit">
                Add chapter
            </UButton>
        </footer>
    </div>
</template>

<script setup lang="ts">
    import type { EditorSuggestionMenuItem, EditorToolbarItem } from "@nuxt/ui";

    import { useEventListener, useStorage } from "@vueuse/core";
    import { computed, ref } from "vue";
    import { useRouter } from "vue-router";

    import { useImportChapters } from "@/composables/useChapters";
    import { useNovel } from "@/composables/useNovels";
    import { importChaptersSchema } from "@/schemas/novel.schema";
    import { formatCount } from "@/utils/format";
    import { scriptLangFor, scriptLangIf } from "@/utils/language";


    const props = defineProps<{ novelId: string }>();

    const router = useRouter();
    const id = computed(() => Number(props.novelId));

    const { data: novelData } = useNovel(id);
    const { mutateAsync, isPending } = useImportChapters(id);

    const novel = computed(() => novelData.value ?? null);
    const scriptLang = computed(() => (novel.value === null ? undefined : scriptLangFor(novel.value.sourceLanguage)));

    // The draft lives in local storage, keyed per novel, so a reload or a walk off to check the
    // chapter list does not throw away several thousand pasted characters. It is the reason this
    // page asks no "leave and lose it?" question: there is nothing to lose.
    const draft = useStorage(`nekto:chapter-draft:${props.novelId}`, { title: "", body: "" });

    const problem = ref<string | null>(null);
    const problemField = ref<"title" | "body" | null>(null);

    const plainText = computed(() => draft.value.body
        .replace(/<\/(?:p|h[1-6]|li|blockquote)>/gi, "\n")
        .replace(/<[^>]*>/g, "")
        .replace(/&nbsp;/g, " "));

    const counts = computed(() => ({
        characters: plainText.value.replace(/\s+/g, " ").trim().length,
        paragraphs: plainText.value.split("\n").filter(line => line.trim() !== "").length,
    }));

    const hasWork = computed(() => draft.value.title.trim() !== "" || counts.value.characters > 0);

    // Deliberately limited to what survives the server's sanitizer: paragraphs, headings, lists,
    // rules, emphasis, links and images. Offering a control whose formatting is stripped the moment
    // the chapter is saved would be a promise the backend does not keep.
    const toolbar: EditorToolbarItem[][] = [
        [
            { kind: "undo", icon: "i-material-symbols:undo-rounded", tooltip: { text: "Undo" } },
            { kind: "redo", icon: "i-material-symbols:redo-rounded", tooltip: { text: "Redo" } },
        ],
        [
            {
                icon: "i-material-symbols:format-h1-rounded",
                tooltip: { text: "Heading" },
                content: { align: "start" },
                items: [
                    { kind: "heading", level: 2, icon: "i-material-symbols:format-h2-rounded", label: "Heading" },
                    { kind: "heading", level: 3, icon: "i-material-symbols:format-h3-rounded", label: "Subheading" },
                    { kind: "paragraph", icon: "i-material-symbols:format-paragraph-rounded", label: "Body text" },
                ],
            },
            {
                kind: "bulletList",
                icon: "i-material-symbols:format-list-bulleted-rounded",
                tooltip: { text: "Bulleted list" },
            },
            {
                kind: "orderedList",
                icon: "i-material-symbols:format-list-numbered-rounded",
                tooltip: { text: "Numbered list" },
            },
            {
                kind: "horizontalRule",
                icon: "i-material-symbols:horizontal-rule-rounded",
                tooltip: { text: "Scene break" },
            },
        ],
        [
            { kind: "mark", mark: "bold", icon: "i-material-symbols:format-bold-rounded", tooltip: { text: "Bold" } },
            {
                kind: "mark",
                mark: "italic",
                icon: "i-material-symbols:format-italic-rounded",
                tooltip: { text: "Italic" },
            },
        ],
        [
            { kind: "link", icon: "i-material-symbols:link-rounded", tooltip: { text: "Link" } },
            { kind: "image", icon: "i-material-symbols:image-outline-rounded", tooltip: { text: "Image" } },
        ],
    ];

    const commands: EditorSuggestionMenuItem[][] = [
        [
            { type: "label", label: "Text" },
            { kind: "paragraph", label: "Body text", icon: "i-material-symbols:format-paragraph-rounded" },
            { kind: "heading", level: 2, label: "Heading", icon: "i-material-symbols:format-h2-rounded" },
            { kind: "heading", level: 3, label: "Subheading", icon: "i-material-symbols:format-h3-rounded" },
        ],
        [
            { type: "label", label: "Lists" },
            { kind: "bulletList", label: "Bulleted list", icon: "i-material-symbols:format-list-bulleted-rounded" },
            { kind: "orderedList", label: "Numbered list", icon: "i-material-symbols:format-list-numbered-rounded" },
        ],
        [
            { type: "label", label: "Insert" },
            { kind: "horizontalRule", label: "Scene break", icon: "i-material-symbols:horizontal-rule-rounded" },
            { kind: "image", label: "Image", icon: "i-material-symbols:image-outline-rounded" },
        ],
        [
            { type: "label", label: "Block" },
            { kind: "moveUp", label: "Move up", icon: "i-material-symbols:arrow-upward-rounded" },
            { kind: "moveDown", label: "Move down", icon: "i-material-symbols:arrow-downward-rounded" },
            { kind: "duplicate", label: "Duplicate", icon: "i-material-symbols:content-copy-outline-rounded" },
            { kind: "delete", label: "Delete", icon: "i-material-symbols:delete-outline-rounded" },
        ],
    ];


    function clearProblem(): void {
        problem.value = null;
        problemField.value = null;
    }


    function fail(field: "title" | "body", message: string): void {
        problem.value = message;
        problemField.value = field;
    }


    function discard(): void {
        draft.value = { title: "", body: "" };
        clearProblem();
    }


    async function submit(): Promise<void> {
        const result = importChaptersSchema.safeParse({
            title: draft.value.title.trim(),
            body: counts.value.characters === 0 ? "" : draft.value.body,
        });

        if (!result.success) {
            const issue = result.error.issues[0];
            fail(issue?.path[0] === "body" ? "body" : "title", issue?.message ?? "Something is missing.");
            return;
        }

        clearProblem();

        const [chapter] = await mutateAsync([{ title: result.data.title, html: result.data.body }]);

        // Cleared only once the chapter is actually stored. A failed request leaves the draft where
        // it was, which is the whole point of keeping one.
        discard();

        await router.push(chapter
            ? { name: "reader", params: { novelId: props.novelId, chapterId: chapter.id } }
            : { name: "novel", params: { novelId: props.novelId } });
    }


    useEventListener(window, "keydown", (event: KeyboardEvent) => {
        if ((event.metaKey || event.ctrlKey) && event.key === "Enter") {
            event.preventDefault();
            void submit();
        }
    });
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .import {
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

            .counts {
                flex: none;
                color: var(--ui-text-muted);
            }

            .spacer {
                flex: 1;
            }
        }

        // A chapter is thousands of characters. It gets the whole window, on a surface that looks
        // like the one it will be read on, rather than a box in the middle of a dimmed screen.
        //
        // Wider than the reading measure on purpose: this is a working surface for pasting and
        // checking, not a column to read for hours.
        .sheet {
            flex: 1;
            min-height: 0;
            display: flex;
            flex-direction: column;
            width: 100%;
            max-width: 76rem;
            margin: 0 auto;
            padding: 2rem 2.5rem 0;

            .field {
                display: block;
                flex: none;
            }

            .title {
                width: 100%;
                padding: 0.25rem 0.5rem;
                margin: 0 0 0.25rem -0.5rem;
                border: 0;
                border-radius: 0.25rem;
                background: transparent;
                font-family: var(--font-prose);
                font-size: var(--nt-text-xl);
                font-weight: 500;
                line-height: 1.3;
                color: var(--ui-text-highlighted);

                &::placeholder {
                    color: var(--ui-text-dimmed);
                }

                // The default outline was removed for a flush heading, so a replacement is
                // mandatory rather than optional — this is the only thing a keyboard user has.
                &:focus-visible {
                    outline: 2px solid var(--ui-primary);
                    outline-offset: 2px;
                }

                &[aria-invalid="true"] {
                    box-shadow: inset 0 -2px 0 var(--ui-error);
                }
            }

            .editor {
                flex: 1;
                min-height: 0;
                display: flex;
                flex-direction: column;
                position: relative;
            }

            .toolbar {
                flex: none;
                overflow-x: auto;
                padding-bottom: 0.5rem;
                border-bottom: 1px solid var(--ui-border);
            }
        }

        .problem {
            flex: none;
            margin: 0 0 0.5rem;
            color: var(--ui-error);
        }

        .foot {
            flex: none;
            display: flex;
            align-items: center;
            gap: 1rem;
            padding: 0.75rem 2.5rem;
            border-top: 1px solid var(--ui-border);

            .note {
                max-width: 42rem;
                margin: 0;
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);
            }

            .kept {
                flex: none;
                display: inline-flex;
                align-items: center;
                gap: 0.375rem;
                font-size: var(--nt-text-sm);
                color: var(--ui-text-muted);

                .kept-icon {
                    width: 1rem;
                    height: 1rem;
                }
            }

            .spacer {
                flex: 1;
            }
        }
    }

    .visually-hidden {
        position: absolute;
        width: 1px;
        height: 1px;
        margin: -1px;
        padding: 0;
        overflow: hidden;
        clip-path: inset(50%);
        white-space: nowrap;
    }
</style>

<style lang="scss">
    // The editor's own content area is rendered inside the component, so it cannot be reached from a
    // scoped block. It is set in the reading face at the reading size: what the chapter looks like
    // while it is being pasted should be what it looks like once it is stored.
    .chapter-editor-content {
        flex: 1;
        min-height: 0;
        overflow-y: auto;
        padding: 1.5rem 0 5rem;
        font-family: var(--font-prose);
        font-size: var(--nt-prose-size);
        line-height: 1.5;
        color: var(--ui-text);

        p {
            margin: 0 0 1.5em;
        }

        h2,
        h3 {
            margin: 1.5em 0 0.75em;
            font-weight: 500;
            color: var(--ui-text-highlighted);
        }

        h2 {
            font-size: var(--nt-text-lg);
        }

        h3 {
            font-size: var(--nt-text-md);
        }

        ul,
        ol {
            margin: 0 0 1.5em;
            padding-left: 1.5em;
        }

        hr {
            margin: 2em 0;
            border: 0;
            border-top: 1px solid var(--ui-border);
        }

        img {
            max-width: 100%;
            height: auto;
        }
    }
</style>
