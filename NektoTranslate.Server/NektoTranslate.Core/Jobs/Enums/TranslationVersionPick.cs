namespace NektoTranslate.Jobs.Enums;


// Which rendering of a chapter a repair or a voice-learning run reads. Current is the default and
// the only choice Translate ever means; First and Newest exist for the one case a reader actually
// needs them - starting over from what a site gave, or building on the latest machine pass, rather
// than reading whatever an earlier repair already rewrote.
public enum TranslationVersionPick {
    Current = 0,
    First = 1,
    Newest = 2
}
