<template>
    <UPopover :content="{ align: 'end' }">
        <UButton
            icon="i-material-symbols:tune-rounded"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Reading settings"
        />

        <template #content>
            <div class="settings">
                <!-- Width, alignment and offset only make sense with one column. Side by side, the
                     page is already halved, and a width control would be a second way of saying it. -->
                <section v-if="reader.mode !== 'bilingual'" class="group">
                    <span class="heading">Column</span>

                    <div class="row">
                        <span class="label">Position</span>

                        <!-- Plain buttons rather than a UFieldGroup: the group injects context that
                             the popover's own field-group reset sits on top of, and nesting the two
                             crashed Vue while the teleported content was updating. -->
                        <span class="choices">
                            <UButton
                                v-for="option in alignOptions"
                                :key="option.value"
                                size="xs"
                                :icon="option.icon"
                                :color="reader.align === option.value ? 'primary' : 'neutral'"
                                :variant="reader.align === option.value ? 'solid' : 'outline'"
                                :aria-label="option.label"
                                :aria-pressed="reader.align === option.value"
                                @click="reader.setAlign(option.value)"
                            />
                        </span>
                    </div>

                    <div class="row">
                        <span class="label">Width</span>
                        <USlider
                            :model-value="reader.widthPercent"
                            :min="20"
                            :max="100"
                            :step="5"
                            size="sm"
                            class="slider"
                            @update:model-value="reader.setWidthPercent"
                        />
                        <span class="value">{{ reader.widthPercent }}%</span>
                    </div>

                    <div class="row">
                        <span class="label">Offset</span>

                        <USlider
                            :model-value="reader.offsetPercent"
                            :min="0"
                            :max="maxOffset"
                            :step="1"
                            :disabled="reader.align === 'center'"
                            size="sm"
                            class="slider"
                            @update:model-value="reader.setOffsetPercent"
                        />

                        <span class="value">{{ reader.offsetPercent }}%</span>
                    </div>

                    <p class="note">{{ offsetNote }}</p>
                </section>

                <section class="group">
                    <span class="heading">Text</span>

                    <div class="row">
                        <span class="label">Size</span>
                        <USlider
                            :model-value="reader.fontSize"
                            :min="12"
                            :max="26"
                            :step="1"
                            size="sm"
                            class="slider"
                            @update:model-value="reader.setFontSize"
                        />
                        <span class="value">{{ reader.fontSize }}px</span>
                    </div>

                    <div class="row">
                        <span class="label">Leading</span>

                        <USlider
                            :model-value="reader.lineHeight"
                            :min="1.2"
                            :max="2.4"
                            :step="0.1"
                            size="sm"
                            class="slider"
                            @update:model-value="reader.setLineHeight"
                        />

                        <span class="value">{{ reader.lineHeight.toFixed(1) }}</span>
                    </div>

                    <div class="row">
                        <span class="label">Bold</span>
                        <USwitch v-model="reader.bold" size="sm" class="slider" />
                    </div>
                </section>

                <div class="actions">
                    <UButton size="xs" color="neutral" variant="ghost" @click="reader.reset()">
                        Reset
                    </UButton>
                </div>
            </div>
        </template>
    </UPopover>
</template>

<script setup lang="ts">
    import type { ReaderAlign } from "@/stores/reader.store";

    import { computed } from "vue";
    import { useReaderStore } from "@/stores/reader.store";


    const reader = useReaderStore();

    const alignOptions: { value: ReaderAlign; label: string; icon: string }[] = [
        { value: "left", label: "Pin to the left", icon: "i-material-symbols:format-align-left-rounded" },
        { value: "center", label: "Centre", icon: "i-material-symbols:format-align-center-rounded" },
        { value: "right", label: "Pin to the right", icon: "i-material-symbols:format-align-right-rounded" },
    ];

    // The column plus its offset cannot exceed the page, so the offset is capped by whatever width
    // leaves behind. Without this the text could be pushed clean off the screen.
    const maxOffset = computed(() => Math.max(0, 100 - reader.widthPercent));

    const offsetNote = computed(() => (reader.align === "center"
        ? "A centred column has no edge to be pushed away from."
        : `Measured from the ${reader.align} edge, the one the column is pinned to.`));
</script>

<style scoped lang="scss">
    .settings {
        display: flex;
        flex-direction: column;
        gap: 1.25rem;
        width: 22rem;
        padding: 1rem;

        .group {
            display: flex;
            flex-direction: column;
            gap: 0.625rem;
        }

        .heading {
            color: var(--ui-text-dimmed);
        }

        .row {
            display: flex;
            align-items: center;
            gap: 0.75rem;

            .label {
                flex: none;
                width: 4.5rem;
                color: var(--ui-text-muted);
            }

            .slider {
                flex: 1;
                min-width: 0;
            }

            .choices {
                display: flex;
                gap: 0.25rem;
            }

            // Fixed width and tabular figures, so the number does not shift the slider as it counts
            // up while being dragged.
            .value {
                flex: none;
                width: 3rem;
                text-align: right;
                color: var(--ui-text);
            }
        }

        .note {
            margin: 0;
            font-size: var(--nt-text-sm);
            color: var(--ui-text-dimmed);
        }

        .actions {
            display: flex;
            justify-content: flex-end;
        }
    }
</style>
