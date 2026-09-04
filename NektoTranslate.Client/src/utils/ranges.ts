// A print-dialog range over a list: "1-20", "7", "1-20, 25, 30-32". Positions are 1-based because
// that is how the list is numbered on screen. Parts that do not parse, and positions past the end of
// the list, are dropped rather than refused — the field exists to save twenty clicks, and a shortcut
// that argues with a typo is slower than the clicks it replaced.
export function parseRanges(spec: string, max: number): number[] {
    const picked = new Set<number>();

    for (const part of spec.split(",")) {
        const text = part.trim();

        if (text === "") {
            continue;
        }

        const match = /^(\d+)(?:\s*[-–]\s*(\d+))?$/.exec(text);

        if (match === null) {
            continue;
        }

        const first = Number(match[1]);
        const last = match[2] === undefined ? first : Number(match[2]);

        for (let at = Math.min(first, last); at <= Math.max(first, last); at++) {
            if (at >= 1 && at <= max) {
                picked.add(at);
            }
        }
    }

    return [...picked].sort((left, right) => left - right);
}
