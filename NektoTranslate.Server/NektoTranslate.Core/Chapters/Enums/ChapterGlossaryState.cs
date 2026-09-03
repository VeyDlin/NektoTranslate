namespace NektoTranslate.Chapters.Enums;


// Whether the agent has read this chapter and extracted its terms. Independent of translation
// state: a chapter can be analyzed without being translated, and translated without being
// analyzed - the latter is exactly what an imported third-party translation looks like.
public enum ChapterGlossaryState {
    NotAnalyzed = 0,
    Analyzed = 1
}
