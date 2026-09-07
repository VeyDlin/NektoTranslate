import type { StatusMessage } from "@/types/models/domain";


// The SignalR contract, mirrored from ITranslationNotifier on the server.
//
// Payload enums arrive as strings here, unlike the same enums over REST. Decode with @/utils/wire
// rather than comparing raw values.

export interface JobStateChangedEvent {
    jobId: number;
    state: string;
    processed: number;
    total: number;
    costUsd: number;
    currentStep: string | null;
    stepIndex: number | null;
    stepCount: number | null;
}


export interface ChapterStateChangedEvent {
    chapterId: number;
    state: string;
}


// Prose as it is written, markers already stripped. Lets a reader watch a chapter appear instead of
// staring at a spinner for the minute it takes.
export interface TranslationDeltaEvent {
    chapterId: number;
    text: string;
}


export interface ChapterTranslatedEvent {
    chapterId: number;
}


export interface GlossaryChangedEvent {
    sourceTerm: string;
    targetTerm: string;
    origin: string;
}


export interface AgentMessageEvent {
    message: string;
}


// Mirrors ImportJob's own progress fields, pushed on every state or count change so the strip and
// the import screens never have to poll for them.
export interface ImportStateChangedEvent {
    jobId: number;
    kind: string;
    state: string;
    processed: number;
    total: number;
    currentTitle: string | null;
}


// One entry of the batch the user picked has settled — imported, skipped, failed or cancelled. The
// per-chapter list is built from a stream of these rather than from a final result, the same way
// `ChapterStateChanged` builds the chapter table without a full refetch per chapter.
export interface ImportItemFinishedEvent {
    jobId: number;
    position: number;
    sourceUrl: string;
    title: string;
    chapterIndex: number | null;
    chapterId: number | null;
    state: string;
    status: StatusMessage | null;
    finishedAt: string;
}


export interface TranslationEvents {
    JobStateChanged: JobStateChangedEvent;
    ChapterStateChanged: ChapterStateChangedEvent;
    TranslationDelta: TranslationDeltaEvent;
    ChapterTranslated: ChapterTranslatedEvent;
    GlossaryChanged: GlossaryChangedEvent;
    AgentMessage: AgentMessageEvent;
    ImportStateChanged: ImportStateChangedEvent;
    ImportItemFinished: ImportItemFinishedEvent;
    ListingStateChanged: ListingStateChangedEvent;
}


// The read of a site's contents landed, failed, or was abandoned. Carries no entries: the screen
// refetches the listing, which is the one place the entries live, rather than assembling them from
// a stream the way the per-chapter import report is assembled.
export interface ListingStateChangedEvent {
    kind: string;
    state: string;
    entryCount: number;
}


export type TranslationEventName = keyof TranslationEvents;


// One group per novel on the server, so the stream is opened against one novel and closed when the
// user leaves it. `on` returns its own unsubscribe rather than requiring the handler reference back.
export interface TranslationStream {
    watch: (novelId: number) => Promise<void>;
    unwatch: (novelId: number) => Promise<void>;
    on: <K extends TranslationEventName>(event: K, handler: (payload: TranslationEvents[K]) => void) => () => void;
    dispose: () => Promise<void>;
}
