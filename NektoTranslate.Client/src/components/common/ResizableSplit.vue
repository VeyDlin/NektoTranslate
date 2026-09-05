<template>
    <div ref="containerRef" class="split">
        <!-- Hidden together with the handle rather than shrunk to nothing, so a screen with no job of
             this kind yet never carries a pane with no content and a handle with nothing above it to
             resize. -->
        <div v-if="topVisible" ref="topRef" class="pane top" :style="topStyle">
            <slot name="top" />
        </div>

        <div
            v-if="topVisible"
            class="handle"
            :class="{ dragging }"
            role="separator"
            aria-orientation="horizontal"
            aria-valuemin="0"
            aria-valuemax="100"
            :aria-valuenow="sharePercent"
            aria-label="Resize the pane above"
            tabindex="0"
            @pointerdown="onPointerDown"
            @pointermove="onPointerMove"
            @pointerup="onPointerUp"
            @pointercancel="onPointerUp"
            @keydown="onKeydown"
        >
            <span class="line" />
        </div>

        <!-- Always the bottom pane, whether or not the top one is showing - the caller's own flex
             column keeps working inside it exactly as it did before there was a split at all. -->
        <div class="pane bottom">
            <slot name="bottom" />
        </div>
    </div>
</template>

<script setup lang="ts">
    import { computed, onMounted, ref } from "vue";


    // A fixed boundary between the two halves of the import screens made the run panel's own report
    // and the picker below it fight over one screen: tall enough to read a few hundred rows of
    // "imported / skipped / failed" pushed the chapter table below the fold, short enough to keep the
    // table in view buried the report behind its own scrollbar. Neither half is optional and neither
    // should win by default, so the fix is a boundary the reader drags to the size they need right now
    // rather than another collapse standing in for the same choice. This component only owns that
    // boundary - the two slots keep exactly the layout they already had.
    //
    // The height the reader drags to is a ceiling rather than a size. A report shorter than it - a
    // job that has only just started, or one whose report is folded away - leaves the boundary
    // hugging the report instead of holding a blank pane open under it, and a report longer than it
    // scrolls under the boundary. Only while the handle is held does the pane take the height
    // outright, so the boundary follows the pointer one for one; on release it settles back onto the
    // content if there is less of it than room.
    const props = withDefaults(defineProps<{
        storageKey: string;
        topVisible: boolean;
        minTop?: number;
        minBottom?: number;
    }>(), {
        // Enough for the panel's head, its bar, its summary and a row or two of the report: a
        // boundary dragged all the way up still leaves the report readable rather than a bare
        // summary with an empty box under it. Folding the report away is the chevron's job.
        minTop: 160,
        minBottom: 160,
    });

    // The hit area's own height, in pixels. Fixed, so the rule that nothing but the drag itself may
    // change size holds for the handle too - it never grows to announce that it is active.
    const HANDLE_HEIGHT = 8;
    const ARROW_STEP = 24;
    const DEFAULT_SHARE = 0.4;

    const containerRef = ref<HTMLElement | null>(null);
    const topRef = ref<HTMLElement | null>(null);
    const topHeight = ref(0);
    const containerHeight = ref(0);
    const dragging = ref(false);

    const topStyle = computed(() => (
        dragging.value ? { height: `${topHeight.value}px` } : { maxHeight: `${topHeight.value}px` }
    ));

    // Plain closures rather than refs: they only ever matter between one pointerdown and the pointerup
    // that follows it, and reacting to them would buy nothing.
    let pointerStartY = 0;
    let pointerStartHeight = 0;


    // The top pane's ceiling: as tall as the container allows once the bottom pane has kept its own
    // minimum and the handle has kept its 8px. Takes the container height as an argument rather than
    // reading it off state, because a boundary the user placed should hold still when the window
    // changes shape and move again only when they drag it - the height passed in is a fresh
    // measurement taken right before it is needed, at the start of a drag or a key press, not a value
    // this component keeps in sync with the window on its own.
    function maxTopFor(forContainerHeight: number): number {
        return Math.max(props.minTop, forContainerHeight - props.minBottom - HANDLE_HEIGHT);
    }


    function clamp(value: number, forContainerHeight: number): number {
        return Math.min(Math.max(value, props.minTop), maxTopFor(forContainerHeight));
    }


    function readStoredHeight(): number | null {
        try {
            const raw = localStorage.getItem(props.storageKey);

            if (raw === null) {
                return null;
            }

            const parsed = Number(raw);

            return Number.isFinite(parsed) ? parsed : null;
        }
        catch {
            // A private window, a browser that blocks storage outright and a full quota all fail the
            // same way here: the split falls back to its default share for this visit instead of the
            // remembered one.
            return null;
        }
    }


    function writeStoredHeight(value: number): void {
        try {
            localStorage.setItem(props.storageKey, String(Math.round(value)));
        }
        catch {
            // Losing the memory of where the boundary was is not worth failing the drag over.
        }
    }


    // Read once the container has the height its parent's flex layout gives it. That height does not
    // depend on whether the top pane is currently showing anything, so it is measured the same way
    // whether a job of this kind already exists or arrives only later.
    onMounted(() => {
        containerHeight.value = containerRef.value?.clientHeight ?? 0;

        const stored = readStoredHeight();
        const fallback = containerHeight.value * DEFAULT_SHARE;

        topHeight.value = clamp(stored ?? fallback, containerHeight.value);
    });

    // What the separator reports as its current value: the top pane's height as a share of the whole
    // split, not the raw pixel count, so it reads the same way regardless of how tall the screen is.
    const sharePercent = computed(() => (
        containerHeight.value > 0 ? Math.round((topHeight.value / containerHeight.value) * 100) : 0
    ));


    // The pane's height as drawn right now, which is the ceiling or the content, whichever is less.
    // A drag and a key press both start from here rather than from the ceiling, so the boundary
    // moves from where the reader sees it and never jumps to where a taller report once had it.
    function drawnTopHeight(): number {
        return topRef.value?.clientHeight ?? topHeight.value;
    }


    function onPointerDown(event: PointerEvent): void {
        containerHeight.value = containerRef.value?.clientHeight ?? containerHeight.value;
        pointerStartY = event.clientY;
        pointerStartHeight = clamp(drawnTopHeight(), containerHeight.value);
        topHeight.value = pointerStartHeight;
        dragging.value = true;

        // Captured to the handle itself, so the pointer can leave it - into the report above or the
        // table below - without ever losing the drag.
        (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);

        // Otherwise a fast drag selects the report's own text on the way past it, which fights the
        // drag rather than resizing anything.
        document.body.style.userSelect = "none";
    }


    function onPointerMove(event: PointerEvent): void {
        if (!dragging.value) {
            return;
        }

        topHeight.value = clamp(pointerStartHeight + (event.clientY - pointerStartY), containerHeight.value);
    }


    function onPointerUp(event: PointerEvent): void {
        if (!dragging.value) {
            return;
        }

        dragging.value = false;
        document.body.style.userSelect = "";
        (event.currentTarget as HTMLElement).releasePointerCapture(event.pointerId);
        writeStoredHeight(topHeight.value);
    }


    // One case per key, each returning the height that key means outright rather than stepping toward
    // it - Home and End name the extremes directly, the same way a native slider's own Home and End do.
    function heightForKey(key: string, current: number, forContainerHeight: number): number | null {
        switch (key) {
            case "ArrowUp":
                return clamp(current - ARROW_STEP, forContainerHeight);
            case "ArrowDown":
                return clamp(current + ARROW_STEP, forContainerHeight);
            case "Home":
                return props.minTop;
            case "End":
                return maxTopFor(forContainerHeight);
            default:
                return null;
        }
    }


    function onKeydown(event: KeyboardEvent): void {
        const measured = containerRef.value?.clientHeight ?? containerHeight.value;
        const next = heightForKey(event.key, drawnTopHeight(), measured);

        if (next === null) {
            return;
        }

        event.preventDefault();
        containerHeight.value = measured;
        topHeight.value = next;
        writeStoredHeight(next);
    }
</script>

<style scoped lang="scss">
    .split {
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
        overflow: hidden;

        .pane {
            // A flex column so the panel inside can shrink to the ceiling and scroll its own report,
            // keeping its head in view, instead of the whole panel scrolling under a fixed edge.
            &.top {
                flex: none;
                display: flex;
                flex-direction: column;
                overflow: hidden;
            }

            // The picker's own rows are already a flex column - a fixed status line, a fixed mapping
            // line, a table that stretches to fill whatever is left - and that only keeps working if
            // this wrapper offers the same thing. Nothing about their own layout is redone here.
            &.bottom {
                flex: 1;
                min-height: 0;
                display: flex;
                flex-direction: column;
            }
        }

        .handle {
            flex: none;
            display: flex;
            align-items: center;
            height: 0.5rem;
            cursor: row-resize;
            touch-action: none;

            .line {
                width: 100%;
                height: 1px;
                background: var(--ui-border);
            }

            &:hover .line,
            &.dragging .line {
                background: var(--ui-border-accented);
            }

            &:focus-visible {
                outline: 2px solid var(--ui-primary);
                outline-offset: -2px;
            }
        }
    }
</style>
