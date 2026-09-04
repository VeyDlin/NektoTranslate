import type {
    GlossaryCategory,
    GlossaryOrigin,
    ImportItemState,
    JobState,
    TranslationJobMode,
    TranslationOrigin,
    TranslationState,
} from "@/types/models/domain";


// Spending is shown at the precision the number deserves: a single chapter costs three or four
// cents, and rounding that to $0.03 across a nine-hundred-chapter run hides most of the total.
export function formatCost(value: number | null): string {
    if (value === null) {
        return "—";
    }

    return value < 1 ? `$${value.toFixed(3)}` : `$${value.toFixed(2)}`;
}


export function formatCount(value: number): string {
    return value.toLocaleString("en-GB");
}


// The server counts chapters from zero — an import into an empty book assigns index 0 — and the
// range on a job is stated in the same terms. Readers count from one, and so do the books: the
// chapter carrying index 0 is the one titled "Chapter 1". So the number is shifted everywhere a
// person reads it, and shifted back where a range is sent. Nothing stores the shifted value.
export function chapterNumber(index: number): number {
    return index + 1;
}


// Which parser answered is an implementation detail — "NovelLunarParser" names a class in this
// codebase, not anything the person pasting a link would recognise. The site they typed is what they
// know, and a parser is chosen by host name alone, so the host name is what gets shown.
export function siteLabel(url: string): string {
    try {
        return new URL(url).hostname.replace(/^www\./, "");
    }
    catch {
        return url;
    }
}


export function formatWhen(iso: string): string {
    const then = new Date(iso).getTime();
    const minutes = Math.round((Date.now() - then) / 60_000);

    if (minutes < 1) {
        return "just now";
    }

    if (minutes < 60) {
        return `${minutes} min ago`;
    }

    const hours = Math.round(minutes / 60);

    if (hours < 24) {
        return `${hours} h ago`;
    }

    const days = Math.round(hours / 24);

    return days < 30 ? `${days} d ago` : new Date(iso).toLocaleDateString("en-GB");
}


// The site a stored address points at, as a reader would name it. Anything unparseable is not worth
// putting on screen as itself, so it becomes nothing rather than a broken-looking string.
export function hostOf(url: string | null): string | null {
    if (url === null || url.trim() === "") {
        return null;
    }

    try {
        return new URL(url).host.replace(/^www\./, "");
    }
    catch {
        return null;
    }
}


const TRANSLATION_STATE_LABELS: Record<TranslationState, string> = {
    None: "Not translated",
    Queued: "Queued",
    Running: "Translating",
    Translated: "Translated",
    Failed: "Failed",
};

export function translationStateLabel(state: TranslationState): string {
    return TRANSLATION_STATE_LABELS[state];
}


const JOB_STATE_LABELS: Record<JobState, string> = {
    Queued: "Queued",
    Running: "Translating",
    Paused: "Paused",
    Completed: "Finished",
    Failed: "Failed",
    Cancelled: "Cancelled",
};

export function jobStateLabel(state: JobState): string {
    return JOB_STATE_LABELS[state];
}


// An import carries the same states as a translation run, but a screen fetching chapters that says
// "Translating" reads as the subscription being spent, which is the one thing an import never does.
const IMPORT_STATE_LABELS: Record<JobState, string> = {
    Queued: "Queued",
    Running: "Importing",
    Paused: "Paused",
    Completed: "Finished",
    Failed: "Failed",
    Cancelled: "Cancelled",
};

export function importStateLabel(state: JobState): string {
    return IMPORT_STATE_LABELS[state];
}


// What a run of each kind is doing while it is actually in progress. `jobStateLabel`'s "Translating"
// for JobState.Running predates modes and is still correct for the mode it was written for; this is
// the version a caller reaches for once a job can also be learning a voice or repairing chapters, so
// the run strip and the jobs table stop calling every live run a translation.
const JOB_MODE_PROGRESS_LABELS: Record<TranslationJobMode, string> = {
    Translate: "Translating",
    LearnVoice: "Learning the voice",
    Repair: "Repairing",
};

export function jobModeProgressLabel(mode: TranslationJobMode): string {
    return JOB_MODE_PROGRESS_LABELS[mode];
}


const IMPORT_ITEM_STATE_LABELS: Record<ImportItemState, string> = {
    Pending: "Pending",
    Imported: "Imported",
    Skipped: "Skipped",
    Failed: "Failed",
    Cancelled: "Cancelled",
};

export function importItemStateLabel(state: ImportItemState): string {
    return IMPORT_ITEM_STATE_LABELS[state];
}


// Origin is not a technical field. It is the difference between a name the book itself established
// and a name the model invented, so it is named in those terms rather than in the enum's.
const TRANSLATION_ORIGIN_LABELS: Record<TranslationOrigin, string> = {
    Ai: "Machine translation",
    Imported: "Existing translation",
    Manual: "Hand correction",
};

export function translationOriginLabel(origin: TranslationOrigin): string {
    return TRANSLATION_ORIGIN_LABELS[origin];
}


const GLOSSARY_ORIGIN_LABELS: Record<GlossaryOrigin, string> = {
    AiExtracted: "Chosen by the model",
    FromExistingTranslation: "From the existing translation",
    Manual: "Set by hand",
};

export function glossaryOriginLabel(origin: GlossaryOrigin): string {
    return GLOSSARY_ORIGIN_LABELS[origin];
}


const GLOSSARY_CATEGORY_LABELS: Record<GlossaryCategory, string> = {
    Person: "Person",
    Place: "Place",
    Organization: "Organisation",
    Technique: "Technique",
    Item: "Item",
    Other: "Other",
};

export function glossaryCategoryLabel(category: GlossaryCategory): string {
    return GLOSSARY_CATEGORY_LABELS[category];
}
