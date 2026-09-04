// Common literary source and target languages, offered as a searchable starting point rather than a
// closed set — `CreateNovelModal` pairs this with `create-item` so a pairing that is not on the list
// still works. English names throughout: the model reads the language as a name regardless of which
// script that name is written in, and an English list is the one every translator on this list of
// languages can still read.
export const LANGUAGES: string[] = [
    "Arabic",
    "Armenian",
    "Azerbaijani",
    "Bengali",
    "Bulgarian",
    "Burmese",
    "Chinese",
    "Croatian",
    "Czech",
    "Danish",
    "Dutch",
    "English",
    "Filipino",
    "Finnish",
    "French",
    "Georgian",
    "German",
    "Greek",
    "Hebrew",
    "Hindi",
    "Hungarian",
    "Indonesian",
    "Italian",
    "Japanese",
    "Khmer",
    "Korean",
    "Malay",
    "Mongolian",
    "Norwegian",
    "Persian",
    "Polish",
    "Portuguese",
    "Romanian",
    "Russian",
    "Serbian",
    "Spanish",
    "Swedish",
    "Tamil",
    "Thai",
    "Turkish",
    "Ukrainian",
    "Vietnamese",
];


// Han characters are drawn differently in Japanese and Chinese typography — 直, 骨 and 今 all
// differ — and a browser given untagged text picks whichever regional face it happens to prefer.
// Tagging the element is what makes a Japanese novel look Japanese.
//
// Languages arrive as free text because the model reads them as names, so this matches loosely and
// returns nothing rather than guessing when it does not recognise the value.
export function scriptLangFor(language: string): string | undefined {
    const value = language.trim().toLowerCase();

    if (value.startsWith("ja") || value.includes("japan")) {
        return "ja";
    }

    if (value.startsWith("zh") || value.includes("chin") || value.includes("mandarin")) {
        return "zh";
    }

    if (value.startsWith("ko") || value.includes("korea")) {
        return "ko";
    }

    return undefined;
}


// Ideographic punctuation through kana, Han extension A, unified Han, Hangul syllables, the
// compatibility ideographs, and the full-width forms. Enough to answer "is this string written in
// the original script", which is all it is asked — it identifies the script, never which of the
// three languages the script is being used for.
//
// Held as numbers rather than as a character class because the first range opens on the ideographic
// space: written literally it is an invisible character in the source, and every linter that reads
// this file is right to object to one.
const CJK_RANGES: readonly (readonly [number, number])[] = [
    [0x3000, 0x30FF],
    [0x3400, 0x4DBF],
    [0x4E00, 0x9FFF],
    [0xAC00, 0xD7AF],
    [0xF900, 0xFAFF],
    [0xFF00, 0xFFEF],
];


function isOriginalScript(text: string): boolean {
    for (const character of text) {
        const code: number = character.codePointAt(0) ?? 0;

        if (CJK_RANGES.some(([low, high]) => code >= low && code <= high)) {
            return true;
        }
    }

    return false;
}


// A novel's declared source language describes its prose, not everything shown alongside it: a book
// catalogued by hand usually has a title typed in the reader's own language, and a chapter is named
// "Глава 3" about as often as it carries a title in the original. Tagging such a string with the
// source language is wrong twice over — it misreports the language to anything reading the page
// aloud, and the CJK serifs carry Cyrillic and Latin glyphs of their own which they draw wide and
// badly. So the tag follows the text rather than the book.
//
// Takes the language already resolved by `scriptLangFor`, because every caller has it to hand — a
// component receives it as a prop or computes it once for the novel, and narrowing it per string is
// all that is left to do.
export function scriptLangIf(text: string, lang: string | undefined): string | undefined {
    return lang !== undefined && isOriginalScript(text) ? lang : undefined;
}
