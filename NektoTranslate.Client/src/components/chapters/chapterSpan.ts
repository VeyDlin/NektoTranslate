// One row's identity and whether Shift can sweep across it. A gap row exists only to say "these
// numbers are missing" — it carries no chapter and nothing about it can be selected, so a span that
// passes through one closes over it rather than breaking there.
export interface SpannableRow {
    id: string;
    selectable: boolean;
}


// The ids between `fromId` and `toId` inclusive, read in whatever order `rows` currently gives them
// — the same two ends produce a different span once the list is sorted newest-first, because the
// span is a slice of that order, not a range of chapter numbers. Either end missing from `rows` (a
// row deleted since it was last clicked) yields no span at all, for the caller to fall back on an
// ordinary toggle instead.
export function chapterSpan(rows: readonly SpannableRow[], fromId: string, toId: string): string[] {
    const fromPosition = rows.findIndex(row => row.id === fromId);
    const toPosition = rows.findIndex(row => row.id === toId);

    if (fromPosition === -1 || toPosition === -1) {
        return [];
    }

    const start = Math.min(fromPosition, toPosition);
    const end = Math.max(fromPosition, toPosition);

    return rows
        .slice(start, end + 1)
        .filter(row => row.selectable)
        .map(row => row.id);
}
