import { Marked } from "marked";


// Chapters arrive as Markdown from the server, which produced them with marked under GitHub-flavour
// settings. Parsing them back with the same library and the same flag keeps the round trip honest:
// anything the server chose to emit is something this parser already understands.
//
// Inline HTML passes through untouched, which is the point — furigana survives as <ruby> inside the
// Markdown, and stripping it here would throw away the one annotation this application exists to
// preserve. The markup is safe to trust because the server sanitises every chapter to a small tag
// allowlist on the way in, and nothing else can write to this database.
const reader = new Marked({
    gfm: true,
    breaks: false,
});


export function renderMarkdown(markdown: string): string {
    return reader.parse(markdown, { async: false });
}


// Everything except the words, for counting and for empty checks. Mirrors what the server's
// MarkdownText.Strip does, so both sides agree on what "this chapter is empty" means.
export function markdownToText(markdown: string): string {
    return markdown
        .replace(/<ruby>(.*?)<rt>.*?<\/rt>\s*<\/ruby>/gs, "$1")
        .replace(/<[^>]+>/g, "")
        .replace(/!\[([^\]]*)\]\([^)]*\)/g, "$1")
        .replace(/\[([^\]]*)\]\([^)]*\)/g, "$1")
        .replace(/\*{1,3}([^*]+)\*{1,3}/g, "$1")
        .replace(/^\s{0,3}(?:#{1,6}\s+|>\s?|[-*+]\s+|\d+\.\s+)/gm, "")
        .trim();
}
