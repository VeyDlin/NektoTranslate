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


export interface UpdateSettingsRequest {
    globalStyleGuide?: string | null;
    defaultModel?: string | null;
    glossaryModel?: string | null;
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
