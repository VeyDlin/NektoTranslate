import { defineStore } from "pinia";
import { ref } from "vue";


export type ReaderMode = "source" | "translation" | "bilingual";

export type ReaderAlign = "left" | "center" | "right";


// Reading preferences are the one thing that must survive a reload: nobody wants to re-pick their
// column layout every time they come back to a book.
//
// Width, alignment and offset apply to the single-column modes only. With the original and the
// translation side by side the page is already divided in half, and a second width control on top of
// that would be two ways of saying the same thing.
export const useReaderStore = defineStore("reader", () => {
    const mode = ref<ReaderMode>("bilingual");
    const showRuby = ref(true);

    const widthPercent = ref(55);
    const align = ref<ReaderAlign>("center");

    // Distance from the edge the column is pinned to. Meaningless when centred, because a centred
    // column has no edge to be pushed away from.
    const offsetPercent = ref(0);

    const fontSize = ref(14);
    const lineHeight = ref(1.5);
    const bold = ref(false);


    function setMode(next: ReaderMode): void {
        mode.value = next;
    }


    function setAlign(next: ReaderAlign): void {
        align.value = next;

        if (next === "center") {
            offsetPercent.value = 0;
        }
    }


    function toggleRuby(): void {
        showRuby.value = !showRuby.value;
    }


    // Sliders hand back either a number or a one-element array, depending on the control, and a bad
    // value reaching storage poisons every later session: the settings panel throws on open and the
    // whole popover subtree comes down with it. Everything numeric is funnelled through here.
    function toNumber(value: unknown, fallback: number, min: number, max: number, decimals = 0): number {
        const raw = Array.isArray(value) ? value[0] : value;
        const parsed = typeof raw === "string" ? Number(raw) : raw;

        if (typeof parsed !== "number" || !Number.isFinite(parsed)) {
            return fallback;
        }

        const factor = 10 ** decimals;

        return Math.min(max, Math.max(min, Math.round(parsed * factor) / factor));
    }


    function setWidthPercent(value: unknown): void {
        widthPercent.value = toNumber(value, 55, 20, 100);
        offsetPercent.value = Math.min(offsetPercent.value, 100 - widthPercent.value);
    }


    function setOffsetPercent(value: unknown): void {
        offsetPercent.value = toNumber(value, 0, 0, 100 - widthPercent.value);
    }


    function setFontSize(value: unknown): void {
        fontSize.value = toNumber(value, 14, 12, 26);
    }


    function setLineHeight(value: unknown): void {
        lineHeight.value = toNumber(value, 1.5, 1.2, 2.4, 1);
    }


    // Storage written by an older build — or by a slider that handed back an array before this was
    // guarded — is repaired on the way in rather than left to throw at the first render.
    function normalize(): void {
        setWidthPercent(widthPercent.value);
        setOffsetPercent(offsetPercent.value);
        setFontSize(fontSize.value);
        setLineHeight(lineHeight.value);

        if (!["left", "center", "right"].includes(align.value)) {
            align.value = "center";
        }

        bold.value = bold.value === true;
        showRuby.value = showRuby.value !== false;
    }


    function reset(): void {
        widthPercent.value = 55;
        align.value = "center";
        offsetPercent.value = 0;
        fontSize.value = 14;
        lineHeight.value = 1.5;
        bold.value = false;
    }


    return {
        mode,
        showRuby,
        widthPercent,
        align,
        offsetPercent,
        fontSize,
        lineHeight,
        bold,
        setMode,
        setAlign,
        setWidthPercent,
        setOffsetPercent,
        setFontSize,
        setLineHeight,
        normalize,
        toggleRuby,
        reset,
    };
}, {
    persist: {
        afterHydrate: (context) => {
            (context.store as unknown as { normalize: () => void }).normalize();
        },
    },
});
