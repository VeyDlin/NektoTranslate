<template>
    <UModal
        v-model:open="open"
        title="Add a novel"
        description="Name the book and the languages. You can paste chapters once it exists."
    >
        <template #body>
            <UForm :schema="createNovelSchema" :state="state" class="form" @submit="submit">
                <UFormField name="title" label="Title">
                    <UInput v-model="state.title" placeholder="As it appears on the original" autofocus />
                </UFormField>

                <div class="pair">
                    <UFormField name="sourceLanguage" label="Written in">
                        <USelectMenu
                            v-model="state.sourceLanguage"
                            :items="LANGUAGES"
                            create-item
                            placeholder="Japanese"
                            class="w-full"
                        />
                    </UFormField>

                    <UFormField name="targetLanguage" label="Translate into">
                        <USelectMenu
                            v-model="state.targetLanguage"
                            :items="LANGUAGES"
                            create-item
                            placeholder="English"
                            class="w-full"
                        />
                    </UFormField>
                </div>

                <p class="note">
                    Languages are free text. The model reads them as names, so an unusual pair works
                    as well as a common one.
                </p>

                <UFormField name="sourceUrl" label="Where it came from" hint="Optional">
                    <UInput v-model="state.sourceUrl" placeholder="https://" />
                </UFormField>

                <UFormField
                    name="styleGuide"
                    label="Style notes"
                    hint="Optional"
                    description="Tone, register, how to handle honorifics. Sent with every chapter."
                >
                    <UTextarea v-model="state.styleGuide" :rows="3" />
                </UFormField>

                <div class="actions">
                    <UButton color="neutral" variant="ghost" @click="open = false">
                        Cancel
                    </UButton>

                    <UButton type="submit" :loading="isPending">
                        Add novel
                    </UButton>
                </div>
            </UForm>
        </template>
    </UModal>
</template>

<script setup lang="ts">
    import type { CreateNovelInput } from "@/schemas/novel.schema";
    import { reactive } from "vue";

    import { useRouter } from "vue-router";
    import { useCreateNovel } from "@/composables/useNovels";
    import { createNovelSchema } from "@/schemas/novel.schema";
    import { LANGUAGES } from "@/utils/language";


    const open = defineModel<boolean>("open", { required: true });

    const router = useRouter();
    const { mutateAsync, isPending } = useCreateNovel();

    const state = reactive<CreateNovelInput>({
        title: "",
        sourceLanguage: "",
        targetLanguage: "",
        sourceUrl: "",
        styleGuide: "",
    });


    async function submit(): Promise<void> {
        const novel = await mutateAsync({
            title: state.title,
            sourceLanguage: state.sourceLanguage,
            targetLanguage: state.targetLanguage,
            sourceUrl: state.sourceUrl || null,
            styleGuide: state.styleGuide || null,
        });

        open.value = false;
        await router.push({ name: "novel", params: { novelId: novel.id } });
    }
</script>

<style scoped lang="scss">
    .form {
        display: flex;
        flex-direction: column;
        gap: 1rem;

        .pair {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 1rem;
        }

        .note {
            margin: -0.5rem 0 0;
            font-size: var(--nt-text-sm);
            color: var(--ui-text-muted);
        }

        .actions {
            display: flex;
            justify-content: flex-end;
            gap: 0.5rem;
            margin-top: 0.5rem;
        }
    }
</style>
