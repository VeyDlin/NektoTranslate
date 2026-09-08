<template>
    <div v-if="controlStyle !== null && controlStyle !== 'none'" class="controls" :class="controlStyle">
        <button
            v-for="control in controls"
            :key="control.action"
            type="button"
            class="control"
            :class="{ close: control.action === 'close' }"
            :aria-label="control.label"
            @click="handle(control.action)"
        >
            <span v-if="controlStyle === 'fluent'" class="glyph" aria-hidden="true">{{ control.glyph }}</span>
            <UIcon v-else :name="control.icon" aria-hidden="true" />
        </button>
    </div>
</template>

<script setup lang="ts">
    import { computed } from "vue";
    import { barLayoutFor, useDesktopShell } from "@/desktop/shell";


    type ControlAction = "minimize" | "maximize" | "close";

    interface ControlDescriptor {
        action: ControlAction;
        label: string;
        glyph: string;
        icon: string;
    }


    // Codepoints from Segoe Fluent Icons - what the platform draws its own caption buttons with, and
    // present on every Windows 10/11 install, so nothing here has to ship or load a font. Written as
    // escapes rather than the literal glyphs so the source stays plain ASCII.
    const WindowsGlyph = {
        minimize: String.fromCharCode(0xE921),
        maximize: String.fromCharCode(0xE922),
        restore: String.fromCharCode(0xE923),
        close: String.fromCharCode(0xE8BB),
    } as const;


    const { desktopShell, isMaximized, minimize, toggleMaximize, close } = useDesktopShell();

    const controlStyle = computed(() => (
        desktopShell === null ? null : barLayoutFor(desktopShell.platform).controlStyle
    ));


    const controls = computed<ControlDescriptor[]>(() => [
        {
            action: "minimize",
            label: "Minimize",
            glyph: WindowsGlyph.minimize,
            icon: "i-material-symbols:minimize-rounded",
        },
        {
            action: "maximize",
            label: isMaximized.value ? "Restore" : "Maximize",
            glyph: isMaximized.value ? WindowsGlyph.restore : WindowsGlyph.maximize,
            icon: isMaximized.value
                ? "i-material-symbols:filter-none-rounded"
                : "i-material-symbols:crop-square-rounded",
        },
        {
            action: "close",
            label: "Close",
            glyph: WindowsGlyph.close,
            icon: "i-material-symbols:close-rounded",
        },
    ]);


    function handle(action: ControlAction): void {
        if (action === "minimize") {
            minimize();
        }
        else if (action === "maximize") {
            toggleMaximize();
        }
        else {
            close();
        }
    }
</script>

<style scoped lang="scss">
    .controls {
        display: flex;
        flex: none;
        height: 100%;
        user-select: none;

        // The full 46x48 (round buttons: 26x26) rectangle is the hit area - the glyph or icon inside
        // is only the picture, never a separate inner hit target - so no padding, margin or border
        // of any kind narrows what a click can land on.
        .control {
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 0;
            margin: 0;
            background: none;
            border: none;
            color: var(--ui-text-muted);
            cursor: pointer;

            &:focus-visible {
                outline: 2px solid var(--ui-primary);
                outline-offset: -2px;
            }
        }
    }

    // Windows: the platform's own caption button shape and font, pressed flush against the bar's own
    // right and top edges (AppBar leaves this component with no padding to push it away from either) -
    // a corner pixel has to hit the close button the way it does on every native Windows window. No
    // gap between the three; close hovers to the platform's own red. This is what stood in for the
    // first cut's hand-drawn SVG lines.
    .controls.fluent .control {
        width: 46px;
        height: 100%;
        font-family: "Segoe Fluent Icons", "Segoe MDL2 Assets", sans-serif;
        font-size: 10px;

        &:hover {
            background: var(--ui-bg-accented);
        }

        &.close:hover {
            color: #fff;
            background: #c42b1c;
        }
    }

    // Linux (GNOME): round, filled rather than only on hover. The 6px right padding is a deliberate
    // exception to "flush with the edge" above - a round button pressed exactly into the window's
    // corner would have its own curve clipped by it, which reads as cut off rather than placed.
    .controls.round {
        gap: 0.5rem;
        padding-right: 6px;

        .control {
            width: 26px;
            height: 26px;
            background: var(--ui-bg-accented);
            border-radius: 50%;

            &:hover {
                filter: brightness(1.08);
            }

            &.close:hover {
                color: #fff;
                background: #c42b1c;
            }
        }
    }
</style>
