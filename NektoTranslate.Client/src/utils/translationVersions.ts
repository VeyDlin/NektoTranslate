import type { ChapterTranslation, TranslationVersionPick } from "@/types/models/domain";

import { formatCount } from "./format";


// The reader's own default choice: the version everyone reading this chapter reads unless they pick
// otherwise. isCurrent is what decides it now, not position in the list - translations arrive
// newest-first, and the current one is rarely translations[0] once something has been pinned or a
// bulk "use first version" has run. Falling back to the newest (translations[0]) covers the moment
// before a chapter's data has isCurrent set at all, which should not happen past the migration but
// is cheap to be honest about rather than showing nothing.
export function defaultShownTranslationId(translations: readonly ChapterTranslation[]): number | null {
    const current = translations.find(translation => translation.isCurrent);

    return current?.id ?? translations[0]?.id ?? null;
}


// What the Runs list and the run strip add to a Repair or LearnVoice run's own description when it
// read something other than the current version - nothing for Current, which is silent because it
// is what every run already meant.
const SOURCE_VERSION_PHRASES: Record<TranslationVersionPick, string> = {
    Current: "",
    First: "from the first version",
    Newest: "from the newest version",
};

export function sourceVersionPhrase(pick: TranslationVersionPick): string {
    return SOURCE_VERSION_PHRASES[pick];
}


// The selection bar's bulk "use first/newest version" toast. The skipped clause is dropped entirely
// when nothing was, rather than reading "0 skipped" - a sentence that states a non-event as though
// it were one.
export function bulkCurrentToastTitle(pick: "First" | "Newest", changed: number, skipped: number): string {
    const chapters = `${formatCount(changed)} ${changed === 1 ? "chapter" : "chapters"}`;
    const headline = `${pick} version is now current for ${chapters}`;

    return skipped === 0 ? headline : `${headline}, ${formatCount(skipped)} skipped`;
}
