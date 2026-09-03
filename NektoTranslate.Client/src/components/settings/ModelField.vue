<template>
    <UFormField :label="label" :description="description">
        <div class="field">
            <USelectMenu
                :model-value="modelValue"
                :items="items"
                value-key="id"
                :loading="isLoading"
                placeholder="Choose a model"
                class="picker"
                @update:model-value="onPick"
            />

            <!-- Checking costs a little when the model is valid, so it is a button and never
                 something that happens on its own. -->
            <UButton
                color="neutral"
                variant="subtle"
                :loading="isProbing"
                :disabled="modelValue === ''"
                @click="check"
            >
                Check
            </UButton>
        </div>

        <p v-if="chosen" class="about">{{ chosen.description }}</p>

        <p v-if="probe" class="verdict" :class="{ bad: !probe.available }">
            <template v-if="probe.available">
                {{ probe.id }} answered — your subscription can run it.
            </template>

            <template v-else>
                {{ probe.error ?? "This model was rejected." }}
            </template>
        </p>
    </UFormField>
</template>

<script setup lang="ts">
    import type { ModelOption, ModelProbeResult } from "@/types/models/domain";

    import { computed, ref, watch } from "vue";
    import { useModels, useProbeModel } from "@/composables/useSettings";


    // One field, used for the default model, the glossary model and a novel's own. Typing a model
    // name is a typo that is only discovered halfway through a paid run, so it is never free text.
    const props = defineProps<{
        modelValue: string;
        label: string;
        description: string;
    }>();

    const emit = defineEmits<{ "update:modelValue": [value: string] }>();

    const { data: models, isLoading } = useModels();
    const { mutateAsync: probeModel, isPending: isProbing } = useProbeModel();

    const probe = ref<ModelProbeResult | null>(null);


    // A model saved earlier may not be in the catalogue — the list is configuration and someone may
    // have edited it. Showing it as a choice of its own is better than silently blanking a setting
    // the user is still relying on.
    const items = computed<ModelOption[]>(() => {
        const known: ModelOption[] = models.value ?? [];

        if (props.modelValue === "" || known.some(model => model.id === props.modelValue)) {
            return known;
        }

        return [
            ...known,
            {
                id: props.modelValue,
                label: props.modelValue,
                description: "Not in the configured list. It will still be used as written.",
                isAlias: false,
            },
        ];
    });

    const chosen = computed(() => items.value.find(model => model.id === props.modelValue));


    watch(() => props.modelValue, () => {
        // The old verdict describes the old model, and leaving it on screen would read as if it
        // described the new one.
        probe.value = null;
    });


    function onPick(value: unknown): void {
        emit("update:modelValue", String(value ?? ""));
    }


    async function check(): Promise<void> {
        probe.value = await probeModel(props.modelValue);
    }
</script>

<style scoped lang="scss">
    .field {
        display: flex;
        align-items: center;
        gap: 0.5rem;

        .picker {
            flex: 1;
            min-width: 0;
        }
    }

    .about,
    .verdict {
        margin: 0.5rem 0 0;
        font-size: var(--nt-text-sm);
        line-height: 1.5;
    }

    .about {
        color: var(--ui-text-muted);
    }

    .verdict {
        color: var(--ui-success);

        &.bad {
            color: var(--ui-error);
        }
    }
</style>
