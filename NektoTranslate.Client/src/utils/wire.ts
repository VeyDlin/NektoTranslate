import type {
    ChatRole,
    GlossaryCategory,
    GlossaryOrigin,
    GlossaryState,
    JobScopeKind,
    JobState,
    TranslationIssueState,
    TranslationOrigin,
    TranslationState,
} from "@/types/models/domain";
import {
    CHAT_ROLES,
    GLOSSARY_CATEGORIES,
    GLOSSARY_ORIGINS,
    GLOSSARY_STATES,
    JOB_SCOPE_KINDS,
    JOB_STATES,
    TRANSLATION_ISSUE_STATES,
    TRANSLATION_ORIGINS,
    TRANSLATION_STATES,
} from "@/types/models/domain";


// Both transports now send enum names: the server added JsonStringEnumConverter so REST would stop
// disagreeing with SignalR, which had always sent names. The integer branch stays as a guard against
// an older server build rather than as the expected case.
//
// An unrecognised value falls back instead of throwing: a state we cannot name is not a reason to
// blank the chapter list.
function decodeEnum<T extends readonly string[]>(values: T, value: unknown, fallback: T[number]): T[number] {
    if (typeof value === "number") {
        return values[value] ?? fallback;
    }

    if (typeof value === "string" && (values as readonly string[]).includes(value)) {
        return value as T[number];
    }

    return fallback;
}


export function decodeTranslationState(value: unknown): TranslationState {
    return decodeEnum(TRANSLATION_STATES, value, "None");
}


export function decodeGlossaryState(value: unknown): GlossaryState {
    return decodeEnum(GLOSSARY_STATES, value, "NotAnalyzed");
}


export function decodeJobState(value: unknown): JobState {
    return decodeEnum(JOB_STATES, value, "Queued");
}


export function decodeJobScopeKind(value: unknown): JobScopeKind {
    return decodeEnum(JOB_SCOPE_KINDS, value, "WholeBook");
}


export function decodeGlossaryOrigin(value: unknown): GlossaryOrigin {
    return decodeEnum(GLOSSARY_ORIGINS, value, "AiExtracted");
}


export function decodeTranslationOrigin(value: unknown): TranslationOrigin {
    return decodeEnum(TRANSLATION_ORIGINS, value, "Ai");
}


export function decodeTranslationIssueState(value: unknown): TranslationIssueState {
    return decodeEnum(TRANSLATION_ISSUE_STATES, value, "Open");
}


export function decodeGlossaryCategory(value: unknown): GlossaryCategory {
    return decodeEnum(GLOSSARY_CATEGORIES, value, "Other");
}


export function decodeChatRole(value: unknown): ChatRole {
    return decodeEnum(CHAT_ROLES, value, "Agent");
}


// The wire wants the integer back. Position in the domain tuple is the contract, matching the
// backend enum declarations one for one.
export function encodeJobScopeKind(value: JobScopeKind): number {
    return JOB_SCOPE_KINDS.indexOf(value);
}
