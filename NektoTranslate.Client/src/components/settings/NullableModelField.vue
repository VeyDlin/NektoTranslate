<template>
    <UFormField :label="label" :description="description">
        <div class="field">
            <USelectMenu
                :model-value="modelValue ?? ''"
                :items="items"
                value-key="id"
                :loading="isLoading"
                class="picker w-full"
                @update:model-value="onPick"
            />

            <!-- Checking costs a little when the model is valid, so it is a button and never
                 something that happens on its own. Nothing to check while the default is chosen. -->
            <UButton
                color="neutral"
                variant="subtle"
                :loading="isProbing"
                :disabled="!modelValue"
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
                {{ probe.error ? describe(probe.error) : "This model was rejected." }}
            </template>
        </p>
    </UFormField>
</template>

<script setup lang="ts">
    import type { ModelOption, ModelProbeResult } from "@/types/models/domain";

    import { computed, ref, watch } from "vue";
    import { useModels, useProbeModel } from "@/composables/useSettings";
    import { describe } from "@/utils/status";


    // Like ModelField, but for a setting whose unset state is itself a real, named choice - "the
    // book's own model" - rather than an empty field waiting to be filled in. repairModel is the one
    // model picker in the app that works this way; every other one always names something.
    const props = defineProps<{
        modelValue: string | null;
        label: string;
        description: string;
        defaultLabel: string;
    }>();

    const emit = defineEmits<{ "update:modelValue": [value: string | null] }>();

    const { data: models, isLoading } = useModels();
    const { mutateAsync: probeModel, isPending: isProbing } = useProbeModel();

    const probe = ref<ModelProbeResult | null>(null);

    const defaultOption = computed<ModelOption>(() => ({
        id: "",
        label: props.defaultLabel,
        description: "No override — the book's own model runs its repairs, whatever that is set to.",
        isAlias: false,
    }));

    // A model saved earlier may not be in the catalogue — the list is configuration and someone may
    // have edited it. Showing it as a choice of its own is better than silently blanking a setting
    // the user is still relying on. The default option always leads the list, so switching back to
    // it is always one click away.
    const items = computed<ModelOption[]>(() => {
        const known: ModelOption[] = models.value ?? [];
        const value = props.modelValue;

        if (value === null || known.some(model => model.id === value)) {
            return [defaultOption.value, ...known];
        }

        return [
            defaultOption.value,
            ...known,
            {
                id: value,
                label: value,
                description: "Not in the configured list. It will still be used as written.",
                isAlias: false,
            },
        ];
    });

    const chosen = computed(() => items.value.find(model => model.id === (props.modelValue ?? "")));


    watch(() => props.modelValue, () => {
        // The old verdict describes the old model, and leaving it on screen would read as if it
        // described the new one.
        probe.value = null;
    });


    function onPick(value: unknown): void {
        const id = String(value ?? "");

        emit("update:modelValue", id === "" ? null : id);
    }


    async function check(): Promise<void> {
        if (!props.modelValue) {
            return;
        }

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
