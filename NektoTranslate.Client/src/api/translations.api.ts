import type { MoveTranslationsRequest } from "@/types/api/requests";
import type {
    AlignmentRow,
    DeleteTranslationsResult,
    MoveTranslationsResult,
} from "@/types/models/domain";

import { apiClient } from "./client";


export const translationsApi = {
    // Both sides of the book, previews only.
    alignment(novelId: number, language: string): Promise<AlignmentRow[]> {
        return apiClient<AlignmentRow[]>(
            `/api/novels/${novelId}/translations/alignment?language=${encodeURIComponent(language)}`,
        );
    },


    // Removes the translation of a span of chapters. Not the chapters themselves — deleting a
    // chapter is a different endpoint and takes the original with it.
    deleteRange(
        novelId: number,
        language: string,
        from: number,
        to: number,
    ): Promise<DeleteTranslationsResult> {
        const query = `language=${encodeURIComponent(language)}&from=${from}&to=${to}`;

        return apiClient<DeleteTranslationsResult>(
            `/api/novels/${novelId}/translations?${query}`,
            { method: "DELETE" },
        );
    },


    // Answers 409 with the collisions when any part of the move conflicts, having changed nothing.
    // ofetch throws on 409, so the caller has to read the body off the error rather than the result
    // — see useAlignment, which normalises both into one shape.
    move(novelId: number, request: MoveTranslationsRequest): Promise<MoveTranslationsResult> {
        return apiClient<MoveTranslationsResult>(`/api/novels/${novelId}/translations/move`, {
            method: "POST",
            body: request,
        });
    },
};
