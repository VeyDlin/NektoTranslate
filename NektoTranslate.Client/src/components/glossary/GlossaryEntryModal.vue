<template>
    <UModal
        v-model:open="open"
        :dismissible="!isPending"
        :title="entry ? 'Edit term' : 'Add a term'"
        :description="entry
            ? 'Saving marks the rendering as set by hand, which outranks anything the model chose.'
            : 'The source form is the entry\'s identity. The rendering is what you are deciding.'"
    >
        <template #body>
            <div class="form">
                <UFormField label="As written in the source" :hint="entry ? 'Fixed' : undefined">
                    <UInput
                        v-model="state.sourceTerm"
                        :disabled="entry !== null"
                        :lang="scriptLang"
                        placeholder="田中太郎"
                    />
                </UFormField>

                <UFormField label="Rendered as">
                    <UInput v-model="state.targetTerm" placeholder="Tanaka Tarou" autofocus />
                </UFormField>

                <UFormField label="Kind">
                    <USelect v-model="state.category" :items="categoryOptions" class="w-full" />
                </UFormField>

                <UFormField
                    label="Other spellings"
                    hint="Optional"
                    description="Separated by commas. What makes the name findable when the source inflects it."
                >
                    <UInput v-model="aliasText" :lang="scriptLang" placeholder="太郎, タロウ" />
                </UFormField>

                <UFormField
                    label="Notes"
                    hint="Optional"
                    description="What the spelling alone does not carry: gender, register, form of address."
                >
                    <UTextarea v-model="state.notes" :rows="3" />
                </UFormField>

                <p v-if="problem" class="problem" role="alert">{{ problem }}</p>

                <div class="actions">
                    <UButton color="neutral" variant="ghost" @click="open = false">
                        Cancel
                    </UButton>

                    <UButton :loading="isPending" @click="submit">
                        {{ entry ? "Save" : "Add term" }}
                    </UButton>
                </div>
            </div>
        </template>
    </UModal>
</template>

<script setup lang="ts">
    import type { GlossaryCategory, GlossaryEntry } from "@/types/models/domain";

    import { reactive, ref, watch } from "vue";
    import { useUpsertGlossaryEntry } from "@/composables/useGlossary";
    import { GLOSSARY_CATEGORIES } from "@/types/models/domain";
    import { glossaryCategoryLabel } from "@/utils/format";


    const props = defineProps<{
        novelId: number;
        entry: GlossaryEntry | null;
        language: string;
        scriptLang?: string;
    }>();

    const open = defineModel<boolean>("open", { required: true });

    const { mutateAsync, isPending } = useUpsertGlossaryEntry(() => props.novelId);

    const state = reactive({
        sourceTerm: "",
        targetTerm: "",
        category: "Other" as GlossaryCategory,
        notes: "",
    });

    const aliasText = ref("");
    const problem = ref<string | null>(null);

    const categoryOptions = GLOSSARY_CATEGORIES.map(category => ({
        value: category,
        label: glossaryCategoryLabel(category),
    }));


    // Reloaded whenever the dialog opens rather than watching the entry alone, so reopening on the
    // same term after an abandoned edit starts from what is stored, not from what was typed.
    watch(open, (isOpen) => {
        if (!isOpen) {
            return;
        }

        problem.value = null;
        state.sourceTerm = props.entry?.sourceTerm ?? "";
        state.targetTerm = props.entry?.targetTerm ?? "";
        state.category = props.entry?.category ?? "Other";
        state.notes = props.entry?.notes ?? "";
        aliasText.value = props.entry?.aliases.join(", ") ?? "";
    });


    async function submit(): Promise<void> {
        const sourceTerm = state.sourceTerm.trim();
        const targetTerm = state.targetTerm.trim();

        if (sourceTerm === "") {
            problem.value = "Give the term as it appears in the source.";
            return;
        }

        if (targetTerm === "") {
            problem.value = "Give the rendering.";
            return;
        }

        problem.value = null;

        await mutateAsync({
            language: props.language,
            sourceTerm,
            targetTerm,
            category: state.category,
            notes: state.notes.trim() === "" ? null : state.notes.trim(),
            aliases: aliasText.value
                .split(",")
                .map(alias => alias.trim())
                .filter(alias => alias !== ""),
        });

        open.value = false;
    }
</script>

<style scoped lang="scss">
    .form {
        display: flex;
        flex-direction: column;
        gap: 1rem;

        .problem {
            margin: 0;
            color: var(--ui-error);
        }

        .actions {
            display: flex;
            justify-content: flex-end;
            gap: 0.5rem;
        }
    }
</style>
