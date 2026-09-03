// Domain enums are string unions here, never the wire numbers.
//
// The backend is inconsistent on purpose-built grounds it has not resolved yet: REST responses
// carry these as integers, SignalR payloads carry them as strings. Normalising at the API boundary
// (see @/schemas) keeps that split from leaking into every component that has to render a state.

export const TRANSLATION_STATES = ["None", "Queued", "Running", "Translated", "Failed"] as const;

export type TranslationState = (typeof TRANSLATION_STATES)[number];


export const GLOSSARY_STATES = ["NotAnalyzed", "Analyzed"] as const;

export type GlossaryState = (typeof GLOSSARY_STATES)[number];


export const JOB_STATES = ["Queued", "Running", "Paused", "Completed", "Failed", "Cancelled"] as const;

export type JobState = (typeof JOB_STATES)[number];


export const JOB_SCOPE_KINDS = ["WholeBook", "Range", "Single", "Selection"] as const;

export type JobScopeKind = (typeof JOB_SCOPE_KINDS)[number];


export const GLOSSARY_ORIGINS = ["AiExtracted", "FromExistingTranslation", "Manual"] as const;

export type GlossaryOrigin = (typeof GLOSSARY_ORIGINS)[number];


export const TRANSLATION_ORIGINS = ["Ai", "Imported", "Manual"] as const;

export type TranslationOrigin = (typeof TRANSLATION_ORIGINS)[number];


export const GLOSSARY_CATEGORIES = ["Person", "Place", "Organization", "Technique", "Item", "Other"] as const;

export type GlossaryCategory = (typeof GLOSSARY_CATEGORIES)[number];


// Tool turns are kept in the transcript deliberately, so the user can see what the agent did rather
// than only what it said.
export const CHAT_ROLES = ["User", "Agent", "Tool"] as const;

export type ChatRole = (typeof CHAT_ROLES)[number];


export interface ChatMessage {
    id: number;
    role: ChatRole;
    text: string;
    costUsd: number;
    createdAt: string;
}


export interface ParsedChapterLink {
    sourceUrl: string;
    title: string;
}


// Which sites the application can read, as one list. The bundled parsers are files on disk; a row
// in the database either replaces one of them or adds a site of its own, and `bundled` says which.
export interface ParserScriptSummary {
    hostName: string;
    bundled: boolean;
    edited: boolean;
    enabled: boolean;
}


// Applies to every book. The book's own style guide comes after it and wins where the two disagree;
// neither can replace the built-in instructions that carry the segment protocol.
export interface AppSettings {
    id: number;
    globalStyleGuide: string | null;
    defaultModel: string;
    glossaryModel: string;
    updatedAt: string;
}


export interface Novel {
    id: number;
    title: string;
    sourceLanguage: string;
    targetLanguage: string;
    sourceUrl: string | null;
    styleGuide: string | null;
    model: string;
    normalizeQuotes: boolean;
    createdAt: string;
}


// The table-of-contents shape. Deliberately carries no chapter body: a two-thousand-chapter novel
// would otherwise send megabytes of prose to render a list.
export interface ChapterSummary {
    id: number;
    index: number;
    title: string;
    glossaryState: GlossaryState;
    translationState: TranslationState;
}


// Markdown, the same format the source is stored in. The server moved both sides off HTML so that
// what a user edits is the format editors exist for.
export interface ChapterTranslation {
    id: number;
    language: string;
    markdown: string;
    origin: TranslationOrigin;
    costUsd: number | null;
    createdAt: string;
}


export interface Chapter extends ChapterSummary {
    sourceMarkdown: string;
    translations: ChapterTranslation[];
}


export interface GlossaryEntry {
    id: number;
    novelId: number;
    language: string;
    sourceTerm: string;
    targetTerm: string;
    category: GlossaryCategory;
    aliases: string[];
    notes: string | null;
    origin: GlossaryOrigin;
    confidence: number;
    needsReview: boolean;
    firstSeenChapterId: number | null;
    updatedAt: string;
}


export interface TranslationJob {
    id: number;
    novelId: number;
    scopeKind: JobScopeKind;
    fromIndex: number | null;
    toIndex: number | null;
    chapterIds: number[];
    state: JobState;
    processedCount: number;
    totalCount: number;
    costUsd: number;
    budgetUsd: number | null;
    createdAt: string;
    startedAt: string | null;
    finishedAt: string | null;
    error: string | null;
}
