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


export interface TranslationEvents {
    JobStateChanged: JobStateChangedEvent;
    ChapterStateChanged: ChapterStateChangedEvent;
    TranslationDelta: TranslationDeltaEvent;
    ChapterTranslated: ChapterTranslatedEvent;
    GlossaryChanged: GlossaryChangedEvent;
    AgentMessage: AgentMessageEvent;
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
