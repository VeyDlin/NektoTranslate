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


// What a job actually does to the chapters in its scope. Translate is the original and only mode
// for a long time, which is why it is the enum's zero value on the server — a request that omits
// `mode` entirely still means what it always meant.
export const JOB_MODES = ["Translate", "LearnVoice", "Repair"] as const;

export type TranslationJobMode = (typeof JOB_MODES)[number];


// Which rendering a Repair or LearnVoice run reads; Translate ignores it. Current is the default and
// the only choice that ever meant anything before this existed.
export const TRANSLATION_VERSION_PICKS = ["Current", "First", "Newest"] as const;

export type TranslationVersionPick = (typeof TRANSLATION_VERSION_PICKS)[number];


export const IMPORT_KINDS = ["Originals", "Translation"] as const;

export type ImportKind = (typeof IMPORT_KINDS)[number];


export const IMPORT_ITEM_STATES = ["Pending", "Imported", "Skipped", "Failed", "Cancelled"] as const;

export type ImportItemState = (typeof IMPORT_ITEM_STATES)[number];


// Reading a site's table of contents, as a state the server keeps rather than an answer held in one
// request. Everything the import screens show about "have we read this site yet" comes from here.
export const LISTING_STATES = ["Reading", "Ready", "Failed"] as const;

export type ListingState = (typeof LISTING_STATES)[number];


// Whether one entry of a listing is already a chapter, previously failed to become one, or neither -
// see `ListingEntry` for how the three are told apart.
export const LISTING_ENTRY_STATES = ["NotImported", "Imported", "Failed"] as const;

export type ListingEntryState = (typeof LISTING_ENTRY_STATES)[number];


export const GLOSSARY_ORIGINS = ["AiExtracted", "FromExistingTranslation", "Manual"] as const;

export type GlossaryOrigin = (typeof GLOSSARY_ORIGINS)[number];


export const TRANSLATION_ORIGINS = ["Ai", "Imported", "Manual"] as const;

export type TranslationOrigin = (typeof TRANSLATION_ORIGINS)[number];


export const GLOSSARY_CATEGORIES = ["Person", "Place", "Organization", "Technique", "Item", "Other"] as const;

export type GlossaryCategory = (typeof GLOSSARY_CATEGORIES)[number];


// Resolved means the text was changed; dismissed means the finding was wrong. Kept apart because
// they say opposite things about the check that raised it, and that is the only evidence there will
// ever be about whether a check earns its place.
export const TRANSLATION_ISSUE_STATES = ["Open", "Resolved", "Dismissed"] as const;

export type TranslationIssueState = (typeof TRANSLATION_ISSUE_STATES)[number];


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


// Anything the server says to a person.
//
// `code` is the stable contract a translation is keyed on; `text` is the server's English, shown
// whenever no translation exists; `args` are the values a translation places in its own word order.
// See @/utils/status for the lookup.
export interface StatusMessage {
    code: string;
    text: string;
    args: Record<string, unknown> | null;
}


// One entry in a model dropdown.
//
// The list is configured on the server rather than discovered: the Claude CLI has no command that
// prints its catalogue, and it only rejects a bad name when one is used. The defaults are family
// aliases, which is why they do not go stale — an alias always resolves to the newest model of its
// family.
export interface ModelOption {
    id: string;
    label: string;
    description: string;
    isAlias: boolean;
}


// Whether this subscription can actually run a model. Spends a little when the model is valid, so
// it belongs behind a button rather than on page load.
export interface ModelProbeResult {
    id: string;
    available: boolean;
    error: StatusMessage | null;
}


// What a locally hosted server reports it has loaded. Unlike the Claude side this is a real
// enumeration — every OpenAI-compatible server answers `GET /v1/models`.
//
// `reachable: false` is an ordinary state, not an error: running without a local model is normal.
export interface LocalModelList {
    reachable: boolean;
    models: string[];
    error: StatusMessage | null;
}


// Everything the user can change from the settings screen. Anything with one right answer stayed in
// the server's own configuration and is deliberately absent here.
//
// The style guide applies to every book; a book's own guide comes after it and wins where the two
// disagree. Neither can replace the built-in instructions that carry the segment protocol.
export interface AppSettings {
    id: number;
    globalStyleGuide: string | null;

    defaultModel: string;
    glossaryModel: string;

    localModelEndpoint: string | null;
    localModelName: string;
    localModelApiKey: string;

    maxOutputTokens: number;
    expansionFactor: number;

    voiceWindowChapters: number;
    voiceWindowParagraphs: number;

    // How a translate or repair run reads a chapter: paragraph by paragraph, with a memo before and
    // a proofread after, the way a careful editor works rather than skimming a whole chapter in one
    // batch sized to maxOutputTokens.
    passSegments: number;
    passContextBefore: number;
    passContextAfter: number;
    thinkingTokens: number;
    proofread: boolean;
    repairModel: string | null;

    pageLoadTimeoutMs: number;
    chatMaxRounds: number;

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


// The library screen's own row shape, from `GET /api/novels`. Counts are computed in SQL alongside
// the novel itself, one query for the whole shelf, rather than a body-less chapter list fetched per
// book just to draw a progress bar - the request-per-row this endpoint exists to avoid.
export interface NovelListItem extends Novel {
    totalChapters: number;
    translatedChapters: number;
    failedChapters: number;

    // A translation job or an import job sits at Queued, Running or Paused for this novel right now.
    // Folded into the same query as the counts above; a per-row call to GET .../activity would put
    // back exactly the N+1 this endpoint was built to remove.
    isActive: boolean;
}


// The table-of-contents shape. Deliberately carries no chapter body: a two-thousand-chapter novel
// would otherwise send megabytes of prose to render a list.
export interface ChapterSummary {
    id: number;
    index: number;
    title: string;
    glossaryState: GlossaryState;
    translationState: TranslationState;

    // False for a chapter that arrived as somebody else's translation with no original anywhere. It
    // can be read, edited and repaired; it cannot be translated, and nothing may offer to.
    hasOriginal: boolean;

    // Whether a rendering is on file, apart from the state. A chapter reads Failed after a repair
    // or a forced re-translation broke on it and still has the rendering it had before: repair and
    // learning can take it, and the state alone said they could not.
    hasTranslation: boolean;

    // True when a current version exists and it is not the newest - a person pinned an older
    // rendering, or a bulk "use first version" ran, and everything written after it is being read
    // past rather than read.
    currentIsOlder: boolean;
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

    // Exactly one translation of a chapter carries this, in each language, whenever any exists. The
    // reader, a repair and voice learning all read this one rather than guessing from createdAt.
    isCurrent: boolean;
}


// What a quality check found in one translation. Never fatal: the chapter is written and the issues
// travel with it, because an imperfect translation is worth far more to the reader than a refused
// one.
//
// `blockIndex` is null when the finding is about the chapter as a whole — an ignored glossary term
// says something about every occurrence of the name, and pointing at one would imply the others
// are fine.
export interface ChapterIssue {
    id: number;
    language: string;
    check: string;
    message: StatusMessage;
    blockIndex: number | null;
    state: TranslationIssueState;
}


export interface Chapter extends ChapterSummary {
    // Null when the book arrived as somebody else's translation and the original is nowhere. Such a
    // chapter is read, edited and repaired like any other — it simply has no left-hand side.
    sourceMarkdown: string | null;
    translations: ChapterTranslation[];

    // Only the open ones, and carried with the chapter rather than fetched separately: the reader
    // needs them at the moment it renders the blocks, and a second request would make a marker
    // beside a paragraph depend on a race.
    issues: ChapterIssue[];
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


// A prose fingerprint of how this book's translation is written, learned once from a chapter range
// that already carries both sides rather than written by hand the way a style guide is. Every
// translation and repair after that can match it instead of drifting chapter to chapter.
export interface VoiceProfile {
    profileId: number;
    summary: string;
    fromChapterIndex: number;
    toChapterIndex: number;
    model: string | null;
    costUsd: number | null;
    createdAt: string;
}


// A name or term the voice pass noticed while reading the learned range, kept apart from the
// glossary because it is not a rendering choice — it is a fact about which terms the existing
// translation actually uses and how often, tracked to gauge coverage rather than to render anything.
// `term` is the target-language spelling exactly as that translation wrote it.
export interface TranslationTerm {
    id: number;
    term: string;
    variants: string[];
    category: GlossaryCategory;
    notes: string | null;
    occurrences: number;
    firstSeenChapterId: number | null;
}


// One row of the alignment screen: an original chapter and whatever translation is attached to it.
//
// Previews rather than prose. Alignment is judged by eye — the question is only "does this
// translation belong to this chapter" — and sending the text itself would cost megabytes on a long
// novel to render a list.
export interface AlignmentRow {
    chapterId: number;
    index: number;
    title: string;
    translationState: TranslationState;
    source: string | null;

    translation: {
        id: number;
        origin: TranslationOrigin;
        text: string;
    } | null;

    // Earlier versions exist but are not sent. A hand correction on top of an import makes this 2.
    versions: number;
}


// Where a move would land and what is in the way. Returned instead of applying whenever any part of
// the move conflicts — the server refuses all of it rather than moving what it can.
export interface TranslationCollision {
    fromIndex: number;
    targetIndex: number;
    reason: StatusMessage;
}


export interface MoveTranslationsResult {
    applied: boolean;
    moved: number;
    collisions: TranslationCollision[];
}


export interface DeleteTranslationsResult {
    chapters: number;
    versions: number;
}


export interface TranslationJob {
    id: number;
    novelId: number;
    mode: TranslationJobMode;
    scopeKind: JobScopeKind;
    fromIndex: number | null;
    toIndex: number | null;
    chapterIds: number[];

    // Which version Repair and LearnVoice read; meaningless for Translate, which always reads what
    // it is about to write over.
    sourceVersion: TranslationVersionPick;
    state: JobState;
    processedCount: number;
    totalCount: number;
    costUsd: number;
    budgetUsd: number | null;
    createdAt: string;
    startedAt: string | null;
    finishedAt: string | null;
    error: string | null;

    // What the run is doing right now, in words a reader understands - "Translating batch 3 of 7",
    // "Aligning names", "Writing the voice profile". Null when nothing is running.
    currentStep: string | null;

    // Position inside the current unit of work - the batch inside the chapter being translated or
    // repaired - so the bar can move between chapters. Null when the unit has no inner steps, or
    // for a LearnVoice run, where processedCount/totalCount already move on every step.
    stepIndex: number | null;
    stepCount: number | null;
}


// One row of the chapter list the user picked when the job was started, tracked individually so a
// batch of a thousand entries can fail nine and land the rest — the same partial-success shape the
// old one-shot import endpoints used to return, now visible while the job is still running instead of
// only in a final result.
export interface ImportJobItem {
    position: number;
    sourceUrl: string;
    title: string;
    chapterIndex: number | null;
    chapterId: number | null;
    state: ImportItemState;
    status: StatusMessage | null;
    finishedAt: string | null;
}


// Importing chapters — from a site, or from a translation the book already has — is a job now, the
// same shape as a translation run: it can be watched, paused and resumed rather than being an
// all-or-nothing request the tab has to stay open for.
//
// `items` is null on the list projection (`GET .../imports`) for the same reason a chapter list has
// no bodies — a book's import history is read far more often than any one job's detail — and carried
// wherever a single job is fetched or started.
export interface ImportJob {
    id: number;
    novelId: number;
    kind: ImportKind;
    language: string | null;
    startAtChapterIndex: number;
    state: JobState;
    processedCount: number;
    totalCount: number;
    currentTitle: string | null;
    createdAt: string;
    startedAt: string | null;
    finishedAt: string | null;
    error: string | null;
    items: ImportJobItem[] | null;
}


// How one entry of a listing stands against the book right now: whether that address is already a
// chapter, why it failed the last time an import tried it, and whether it showed up since the read
// before this one. None of it is stored on the entry itself - it is computed fresh every time the
// listing is read back, from whatever chapters, translations and import history exist at that
// moment, because an import running between two visits to this screen changes every field here
// without the site's own contents changing at all.
export interface ListingEntry {
    sourceUrl: string;
    title: string;
    state: ListingEntryState;
    chapterIndex: number | null;
    chapterId: number | null;
    error: StatusMessage | null;
    isNew: boolean;
}


// A site's contents as read for this book, kept on the server between visits to the screen.
//
// The entries cost a real page load behind a one-tab-per-site queue to obtain, which is why they are
// not held in the component that asked for them: leaving the screen, refreshing it, or pressing the
// button twice all used to end with either nothing on screen or a second visit to the same site.
export interface SourceListing {
    kind: ImportKind;
    url: string;
    state: ListingState;
    entries: ListingEntry[];
    error: StatusMessage | null;
    startedAt: string;
    readAt: string | null;
}


// What is running for a novel right now, in one request — translation and every live import
// together. This is the shape a reloaded page adopts to land back where it was rather than replaying
// the event history from nothing.
export interface Activity {
    translation: TranslationJob | null;
    imports: ImportJob[];
}


// What GET /api/health answers - deliberately just these two fields, matching the server's own
// HealthResponse contract. `version` is the assembly's informational version, "0.1.0" or
// "0.1.0+<sha>" once CI stamps a build.
export interface HealthStatus {
    status: string;
    version: string;
}


// What GET /api/system/update answers - matching the server's own UpdateAvailability contract. For
// a browser user, who has no shell of their own to check GitHub releases for them; the desktop shell
// never calls this, it reads the static latest.json a release attaches instead (see
// src/desktop/updates.ts). `checked: false` means GitHub could not be reached, not that the check
// found nothing - `latest`, `url` and `publishedAt` are all null in that case too.
export interface UpdateAvailability {
    current: string;
    latest: string | null;
    isNewer: boolean;
    url: string | null;
    publishedAt: string | null;
    checked: boolean;
}
