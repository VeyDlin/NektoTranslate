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
// The defaults a fresh reader opens with: a column a little over half the page, pinned to the left
// with a tenth of the page as a margin, in a large face with a tight leading - a book page rather
// than a web page. Kept in one place because `reset` and the first run must agree.
const DefaultWidthPercent = 55;
const DefaultAlign: ReaderAlign = "left";
const DefaultOffsetPercent = 10;
const DefaultFontSize = 25;
const DefaultLineHeight = 1.4;

// Bumped whenever the defaults above change on purpose. A reader who never touched the settings is
// carrying the old defaults in storage, not a preference, and gets the new ones once; a version that
// matches is left alone even when the values happen to equal the old defaults.
const CurrentDefaultsVersion = 2;


export const useReaderStore = defineStore("reader", () => {
    const mode = ref<ReaderMode>("bilingual");
    const showRuby = ref(true);

    const widthPercent = ref(DefaultWidthPercent);
    const align = ref<ReaderAlign>(DefaultAlign);

    // Distance from the edge the column is pinned to. Meaningless when centred, because a centred
    // column has no edge to be pushed away from.
    const offsetPercent = ref(DefaultOffsetPercent);

    const fontSize = ref(DefaultFontSize);
    const lineHeight = ref(DefaultLineHeight);
    const bold = ref(false);

    const defaultsVersion = ref(0);


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
        widthPercent.value = toNumber(value, DefaultWidthPercent, 20, 100);
        offsetPercent.value = Math.min(offsetPercent.value, 100 - widthPercent.value);
    }


    function setOffsetPercent(value: unknown): void {
        offsetPercent.value = toNumber(value, DefaultOffsetPercent, 0, 100 - widthPercent.value);
    }


    function setFontSize(value: unknown): void {
        fontSize.value = toNumber(value, DefaultFontSize, 12, 26);
    }


    function setLineHeight(value: unknown): void {
        lineHeight.value = toNumber(value, DefaultLineHeight, 1.2, 2.4, 1);
    }


    // Storage written by an older build — or by a slider that handed back an array before this was
    // guarded — is repaired on the way in rather than left to throw at the first render.
    function normalize(): void {
        if (defaultsVersion.value !== CurrentDefaultsVersion) {
            reset();
        }

        setWidthPercent(widthPercent.value);
        setOffsetPercent(offsetPercent.value);
        setFontSize(fontSize.value);
        setLineHeight(lineHeight.value);

        if (!["left", "center", "right"].includes(align.value)) {
            align.value = DefaultAlign;
        }

        bold.value = bold.value === true;
        showRuby.value = showRuby.value !== false;
    }


    function reset(): void {
        widthPercent.value = DefaultWidthPercent;
        align.value = DefaultAlign;
        offsetPercent.value = DefaultOffsetPercent;
        fontSize.value = DefaultFontSize;
        lineHeight.value = DefaultLineHeight;
        bold.value = false;
        defaultsVersion.value = CurrentDefaultsVersion;
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
        defaultsVersion,
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
