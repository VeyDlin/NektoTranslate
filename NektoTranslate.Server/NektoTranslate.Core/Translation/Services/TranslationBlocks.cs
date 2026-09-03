using NektoTranslate.Chapters.Contracts;


namespace NektoTranslate.Translation.Services;


// Replacing one block of a stored translation, and rebuilding the two forms it is kept in.
//
// Separate from the editing service and free of the database on purpose: this is where an off-by-one
// would put a correction into the wrong paragraph, and that is worth testing without a server.
//
// The whole thing rests on one invariant, which is why it is spelled out rather than assumed. A
// stored translation is blocks joined by blank lines, one block per source segment; its plain text
// is the same blocks stripped and joined by single newlines. So a block's position is the same
// number in all three places - the Markdown, the plain text, and the blockIndex a quality check
// reported. Break that alignment and a check pointing at block 7 starts editing block 6.
public static class TranslationBlocks {

    public static List<string> Split(string markdown) {
        return MarkdownText.SplitBlocks(markdown);
    }


    // Joined exactly as ChapterSegmenter.Reassemble joins them, so an edited translation is
    // indistinguishable in shape from a freshly translated one.
    public static string Join(IReadOnlyList<string> blocks) {
        return string.Join("\n\n", blocks.Select(block => block.Trim()));
    }


    // Rebuilt from the blocks rather than edited in parallel, which keeps it correct by construction
    // instead of by remembering to update it.
    public static string PlainTextOf(IReadOnlyList<string> blocks) {
        return string.Join("\n", blocks.Select(MarkdownText.Strip));
    }
}
