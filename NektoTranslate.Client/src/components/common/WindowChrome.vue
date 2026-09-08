<template>
    <header
        v-if="desktopShell !== null"
        v-show="!isFullscreen"
        class="window-chrome"
        :class="desktopShell.platform"
        :style="{ height: `${layout.barHeightPx}px` }"
        data-tauri-drag-region
    >
        <!-- No buttons of our own on macOS: the real traffic lights sit in this inset, drawn by the
             OS itself (lib.rs builds this window with title_bar_style Overlay + hidden_title). -->
        <span v-if="desktopShell.platform === 'macos'" class="mac-inset" data-tauri-drag-region />

        <span v-else class="brand" data-tauri-drag-region>
            <img src="/favicon.svg" alt="" width="14" height="14" class="brand-mark" data-tauri-drag-region>
        </span>

        <!-- macOS centres the title in the space after the inset (a spacer on each side); Windows
             and Linux keep it left, immediately after the brand mark, with one spacer after it to
             push the controls to the right. -->
        <span v-if="desktopShell.platform === 'macos'" class="spacer" data-tauri-drag-region />
        <span class="title" data-tauri-drag-region>NektoTranslate</span>
        <span class="spacer" data-tauri-drag-region />

        <div v-if="layout.controlsSide === 'right'" class="controls" :class="desktopShell.platform">
            <button
                v-for="control in controls"
                :key="control.action"
                type="button"
                class="control"
                :class="{ close: control.action === 'close' }"
                :aria-label="control.label"
                @click="handle(control.action)"
            >
                <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true">
                    <line
                        v-if="control.glyph === 'minimize'"
                        x1="1.5"
                        y1="5"
                        x2="8.5"
                        y2="5"
                        stroke="currentColor"
                        stroke-width="1"
                    />

                    <rect
                        v-else-if="control.glyph === 'maximize'"
                        x="1.5"
                        y="1.5"
                        width="7"
                        height="7"
                        fill="none"
                        stroke="currentColor"
                        stroke-width="1"
                    />

                    <g v-else-if="control.glyph === 'restore'">
                        <rect x="3" y="1" width="6" height="6" fill="none" stroke="currentColor" stroke-width="1" />
                        <rect x="1" y="3" width="6" height="6" fill="none" stroke="currentColor" stroke-width="1" />
                    </g>

                    <g v-else>
                        <line x1="1.5" y1="1.5" x2="8.5" y2="8.5" stroke="currentColor" stroke-width="1" />
                        <line x1="8.5" y1="1.5" x2="1.5" y2="8.5" stroke="currentColor" stroke-width="1" />
                    </g>
                </svg>
            </button>
        </div>
    </header>
</template>

<script setup lang="ts">
    import { computed } from "vue";
    import { chromeLayoutFor, useDesktopShell } from "@/desktop/shell";


    type ControlAction = "minimize" | "maximize" | "close";
    type ControlGlyph = "minimize" | "maximize" | "restore" | "close";

    interface ControlDescriptor {
        action: ControlAction;
        label: string;
        glyph: ControlGlyph;
    }

    const { desktopShell, isMaximized, isFullscreen, minimize, toggleMaximize, close } = useDesktopShell();

    const layout = computed(() => chromeLayoutFor(desktopShell?.platform ?? "windows"));

    const controls = computed<ControlDescriptor[]>(() => [
        { action: "minimize", label: "Minimize", glyph: "minimize" },
        {
            action: "maximize",
            label: isMaximized.value ? "Restore" : "Maximize",
            glyph: isMaximized.value ? "restore" : "maximize",
        },
        { action: "close", label: "Close", glyph: "close" },
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
    // Double-click toggles maximize on every platform through Tauri's own drag-region handling
    // (data-tauri-drag-region plus core:window:allow-internal-toggle-maximize) - nothing here has
    // to listen for it.
    .window-chrome {
        position: relative;
        z-index: 3;
        display: flex;
        flex: none;
        align-items: center;
        background: var(--ui-bg-muted);
        user-select: none;

        .spacer {
            flex: 1;
            min-width: 0;
            height: 100%;
        }

        .title {
            overflow: hidden;
            font-size: var(--nt-text-sm);
            color: var(--ui-text-dimmed);
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .brand {
            display: flex;
            flex: none;
            align-items: center;
            padding-left: 0.75rem;

            .brand-mark {
                display: block;
                overflow: hidden;
                border-radius: 5px;
            }
        }

        // Windows and Linux sit the title right after the brand mark rather than centred - it
        // needs its own gap from the icon, which macOS's centring spacer already provides for free.
        &.windows .title,
        &.linux .title {
            margin-left: 0.5rem;
        }

        // macOS reserves 78px on the left for the real traffic lights - nothing of ours may sit
        // there, and the app has to leave it clear itself if the bar height ever changes.
        &.macos .mac-inset {
            flex: none;
            width: 78px;
            height: 100%;
        }

        .controls {
            display: flex;
            flex: none;
            height: 100%;

            .control {
                display: flex;
                align-items: center;
                justify-content: center;
                color: var(--ui-text-muted);
                cursor: pointer;
            }
        }

        // Windows: square caption buttons, close hovers to the platform's own red - the one
        // hard-coded colour in this component, everything else reads a token.
        &.windows .controls {
            .control {
                width: 46px;
                height: 100%;

                &:hover {
                    background: var(--ui-bg-accented);
                }

                &.close:hover {
                    color: #fff;
                    background: #c42b1c;
                }
            }
        }

        // Linux (GNOME): round buttons, filled rather than only on hover.
        &.linux .controls {
            gap: 0.5rem;
            padding-right: 0.5rem;

            .control {
                width: 26px;
                height: 26px;
                background: var(--ui-bg-accented);
                border-radius: 50%;

                &:hover {
                    filter: brightness(1.08);
                }
            }
        }
    }
</style>
