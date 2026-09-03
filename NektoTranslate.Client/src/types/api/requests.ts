import type { GlossaryCategory, JobScopeKind } from "@/types/models/domain";


export interface CreateNovelRequest {
    title: string;
    sourceLanguage: string;
    targetLanguage: string;
    sourceUrl?: string | null;
    styleGuide?: string | null;
}


// `html` accepts markup or plain text: blank-line-separated paragraphs are wrapped server-side, so
// the paste path and a future parser path converge on the same stored shape. `index` is assigned
// automatically when omitted.
export interface ImportedChapter {
    title: string;
    html: string;
    index?: number | null;
    sourceUrl?: string | null;
}


export interface StartTranslationJobRequest {
    scopeKind: JobScopeKind;
    fromIndex?: number | null;
    toIndex?: number | null;
    chapterIds?: number[] | null;
    budgetUsd?: number | null;

    // Includes chapters that are already translated. Not a promise of new text: batches are cached
    // against the instructions that produced them, so a forced run with nothing changed returns the
    // same translation at no cost. Change the glossary, either style guide, the quote setting or the
    // model and it genuinely re-translates.
    force?: boolean | null;
}


// The unique key is (novel, language, source term), so sending an existing source term edits that
// entry instead of creating a rival rendering of the same name.
export interface UpsertGlossaryEntryRequest {
    language: string;
    sourceTerm: string;
    targetTerm: string;
    category?: GlossaryCategory | null;
    notes?: string | null;
    aliases?: string[] | null;
}


// Every field optional, and null means "leave alone" rather than "clear". An empty string is what
// clears a nullable field — the two are deliberately different on the server.
export interface UpdateNovelRequest {
    title?: string | null;
    sourceLanguage?: string | null;
    targetLanguage?: string | null;
    sourceUrl?: string | null;
    styleGuide?: string | null;
    model?: string | null;
    normalizeQuotes?: boolean | null;
}


// Every field optional: null means "leave alone", so one screen can save one field without echoing
// back the rest. An empty string is what clears a nullable field.
//
// Numbers are clamped server-side to a usable range rather than rejected — send 999999 and the
// response comes back with the nearest sane value. Render what comes back rather than what was
// sent, or the user never sees the correction.
export interface UpdateSettingsRequest {
    globalStyleGuide?: string | null;
    defaultModel?: string | null;
    glossaryModel?: string | null;

    localModelEndpoint?: string | null;
    localModelName?: string | null;
    localModelApiKey?: string | null;

    maxOutputTokens?: number | null;
    expansionFactor?: number | null;

    voiceWindowChapters?: number | null;
    voiceWindowParagraphs?: number | null;

    pageLoadTimeoutMs?: number | null;
    chatMaxRounds?: number | null;
}


// Moves the translations of a span of chapters onto a different span.
//
// A span plus an offset rather than a list of pairs, because that is what the interface produces: a
// user selects chapters and drags them a few places. Only chapters in the span that actually hold a
// translation move, so a gap in an imported translation does not drag its neighbours along.
//
// `apply: false` is a dry run — same answer, nothing written. Use it while a drag is in progress so
// a collision is shown before the drop rather than after.
export interface MoveTranslationsRequest {
    language: string;
    fromIndex: number;
    toIndex: number;
    offset: number;
    apply: boolean;
}


export interface ParserSupport {
    supported: boolean;
    parser: string | null;
}


// A batch that fetched forty-nine of fifty is a success with a note, not a failure, so the chapters
// that could not be read come back with their reasons rather than as an exception.
export interface ImportFromUrlResult {
    imported: number;
    failures: string[];
}


export interface UpsertParserScriptRequest {
    scriptSource: string;
    displayName?: string | null;
    enabled?: boolean | null;
}


export interface TranslateTextRequest {
    sourceText: string;
    sourceLanguage: string;
    targetLanguage: string;
}


export interface TranslationResult {
    text: string;
    sessionId: string;
    costUsd: number;
}
