import type { ImportKind, ListingEntry, SourceListing } from "@/types/models/domain";

import { decodeImportKind, decodeListingEntryState, decodeListingState } from "@/utils/wire";
import { apiClient } from "./client";


interface RawListingEntry extends Omit<ListingEntry, "state"> {
    state: number | string;
}


interface RawSourceListing extends Omit<SourceListing, "kind" | "state" | "entries"> {
    kind: number | string;
    state: number | string;
    entries: RawListingEntry[];
}


function toEntry(raw: RawListingEntry): ListingEntry {
    return { ...raw, state: decodeListingEntryState(raw.state) };
}


function toListing(raw: RawSourceListing): SourceListing {
    return {
        ...raw,
        kind: decodeImportKind(raw.kind),
        state: decodeListingState(raw.state),
        entries: raw.entries.map(toEntry),
    };
}


export const listingsApi = {
    // Nothing when this book has never had that site read — an empty workbench, not a missing
    // resource, so the caller gets null rather than an error to handle.
    get(novelId: number, kind: ImportKind): Promise<SourceListing | null> {
        return apiClient<RawSourceListing | null>(`/api/novels/${novelId}/listings/${kind}`)
            .then(raw => (raw === null || raw === undefined ? null : toListing(raw)));
    },

    // Answers as soon as the read is recorded, not when the site has been visited. Everything after
    // that arrives through the hub, which is what lets the user leave the page mid-read.
    read(novelId: number, kind: ImportKind, url: string): Promise<SourceListing> {
        return apiClient<RawSourceListing>(`/api/novels/${novelId}/listings/${kind}/read`, {
            method: "POST",
            body: { url },
        }).then(toListing);
    },

    cancel(novelId: number, kind: ImportKind): Promise<void> {
        return apiClient<void>(`/api/novels/${novelId}/listings/${kind}/cancel`, { method: "POST" });
    },
};
