import type { TranslationTerm, VoiceProfile } from "@/types/models/domain";

import { decodeGlossaryCategory } from "@/utils/wire";
import { apiClient } from "./client";


interface RawTranslationTerm extends Omit<TranslationTerm, "category"> {
    category: number | string;
}


function toTerm(raw: RawTranslationTerm): TranslationTerm {
    return {
        ...raw,
        category: decodeGlossaryCategory(raw.category),
    };
}


export const voiceApi = {
    // Answers with nothing rather than 404 until a learn pass has run — a book with no profile yet
    // is the ordinary starting state, not an error.
    profile(novelId: number, language: string): Promise<VoiceProfile | null> {
        return apiClient<VoiceProfile | null>(
            `/api/novels/${novelId}/voice?language=${encodeURIComponent(language)}`,
        ).then(raw => raw ?? null);
    },

    // Ordered here the same way the screen shows them — most-seen first — is left to the caller
    // rather than assumed of the server, since the point of the list is what it says about coverage,
    // not the order a database happened to store it in.
    terms(novelId: number, language: string): Promise<TranslationTerm[]> {
        return apiClient<RawTranslationTerm[]>(
            `/api/novels/${novelId}/terms?language=${encodeURIComponent(language)}`,
        ).then(rows => rows.map(toTerm));
    },

    removeTerm(novelId: number, termId: number): Promise<void> {
        return apiClient<void>(`/api/novels/${novelId}/terms/${termId}`, {
            method: "DELETE",
        });
    },
};
