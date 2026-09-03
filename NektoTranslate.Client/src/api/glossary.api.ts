import type { UpsertGlossaryEntryRequest } from "@/types/api/requests";
import type { GlossaryEntry } from "@/types/models/domain";

import { decodeGlossaryCategory, decodeGlossaryOrigin } from "@/utils/wire";
import { apiClient } from "./client";


interface RawGlossaryEntry extends Omit<GlossaryEntry, "category" | "origin"> {
    category: number | string;
    origin: number | string;
}


function toEntry(raw: RawGlossaryEntry): GlossaryEntry {
    return {
        ...raw,
        category: decodeGlossaryCategory(raw.category),
        origin: decodeGlossaryOrigin(raw.origin),
    };
}


export const glossaryApi = {
    // The server returns these longest source term first, so a short name cannot bury the longer one
    // that contains it. The order is not re-applied here — one place deciding it is enough.
    list(novelId: number, onlyNeedsReview = false): Promise<GlossaryEntry[]> {
        const query = onlyNeedsReview ? "?onlyNeedsReview=true" : "";

        return apiClient<RawGlossaryEntry[]>(`/api/novels/${novelId}/glossary${query}`).then(rows => rows.map(toEntry));
    },

    // Keyed by the source term, so saving an existing term edits it rather than adding a second
    // rendering of the same name — which is the exact failure the glossary exists to prevent.
    upsert(novelId: number, request: UpsertGlossaryEntryRequest): Promise<GlossaryEntry> {
        return apiClient<RawGlossaryEntry>(`/api/novels/${novelId}/glossary`, {
            method: "PUT",
            body: request,
        }).then(toEntry);
    },

    // Clears the review flag without touching the rendering: the user read what the model invented
    // and is content with it. Deliberately distinct from an edit, because the entry's origin stays
    // AiExtracted — that is still the truth about where the name came from.
    approve(novelId: number, entryId: number): Promise<void> {
        return apiClient<void>(`/api/novels/${novelId}/glossary/${entryId}/approve`, {
            method: "POST",
        });
    },

    remove(novelId: number, entryId: number): Promise<void> {
        return apiClient<void>(`/api/novels/${novelId}/glossary/${entryId}`, {
            method: "DELETE",
        });
    },
};
