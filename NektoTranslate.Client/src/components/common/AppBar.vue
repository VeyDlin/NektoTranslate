<template>
    <header class="bar" v-bind="dragAttrs">
        <!-- The bar proper, one row: everything that was the whole header before the update banner
             found its place under it. Inside the shell this row is the window's top edge, so the
             caption buttons stay flush with the corner whatever appears below. -->
        <div class="row" v-bind="dragAttrs">
            <!-- No content of ours in this inset: the real traffic lights are drawn by the OS itself,
                 over the overlay title bar the shell builds the window with. Flush to the bar's own
                 left edge - nothing, including this component's own padding, sits before it. -->
            <span v-if="layout?.macInset" class="mac-inset" data-tauri-drag-region />

            <!-- The slot's own padded, gapped row - kept apart from the header so the window controls
                 can sit flush against the header's true right and top edges with no padding of the
                 header's own in the way (a browser never renders any, so this is invisible there). -->
            <div class="slot" :class="{ 'no-back': noBack, 'desktop': desktopShell !== null }" v-bind="dragAttrs">
                <slot />
            </div>

            <WindowControls v-if="desktopShell !== null && !isFullscreen" />
        </div>

        <!-- Under the bar, on every screen, because every screen renders this component: the one
             place a line about a new version can sit without a layout above the screens having to
             reach between a screen's bar and its body. Renders nothing while there is no update. -->
        <UpdateBanner />
    </header>
</template>

<script setup lang="ts">
    import { computed } from "vue";

    import UpdateBanner from "@/components/common/UpdateBanner.vue";
    import WindowControls from "@/components/common/WindowControls.vue";
    import { barLayoutFor, useDesktopShell } from "@/desktop/shell";


    withDefaults(defineProps<{
        // Every other screen leads with a back button, whose own touch target already carries half
        // the padding this bar wants at its start. The library has no such button, so its first slot
        // child needs the fuller padding the button would otherwise have supplied.
        noBack?: boolean;
    }>(), { noBack: false });

    const { desktopShell, isFullscreen } = useDesktopShell();

    const layout = computed(() => (desktopShell === null ? null : barLayoutFor(desktopShell.platform)));

    // Tauri only starts a window drag when the *event target itself* carries this attribute - never
    // present outside the shell, so a browser tab never sees it at all. Shared by the header and the
    // slot wrapper below it: a mousedown on either one directly (the header's own bare edges, or the
    // slot's padding and inter-item gaps) has to start a drag exactly as it would if the wrapper did
    // not exist.
    const dragAttrs = computed<Record<string, string>>(() => {
        const attrs: Record<string, string> = {};

        if (desktopShell !== null) {
            attrs["data-tauri-drag-region"] = "";
        }

        return attrs;
    });
</script>

<style scoped lang="scss">
    @use "@/assets/scss/variables" as *;

    .bar {
        flex: none;
        display: flex;
        flex-direction: column;
        border-bottom: 1px solid var(--ui-border);

        .row {
            display: flex;
            align-items: center;
            height: $chrome-height;
        }

        // macOS reserves 78px on the left for the real traffic lights.
        .mac-inset {
            flex: none;
            width: 78px;
            height: 100%;
        }

        .slot {
            flex: 1;
            min-width: 0;
            display: flex;
            align-items: center;
            gap: 0.875rem;
            padding: 0 1rem 0 0.5rem;

            &.no-back {
                padding-left: 1rem;
            }
        }
    }

    // Only inside the shell: a mousedown on the bar's own non-interactive text (titles, counts, the
    // spacer, the language pair) has to fall through to the slot wrapper underneath it, so Tauri
    // sees a drag region as the event target - screens mark that content with [data-bar-text] rather
    // than this component walking their DOM. Buttons, links, selects and inputs are never marked, so
    // they keep their events unconditionally. Gated on .desktop so a browser tab - where, say, the
    // library's version span still wants its native title tooltip on hover - never sees pointer
    // events change at all.
    .slot.desktop {
        user-select: none;

        :slotted([data-bar-text]) {
            pointer-events: none;
        }
    }
</style>
