import type { ImportedChapter } from "@/types/api/requests";

import type { Chapter, ChapterSummary } from "@/types/models/domain";
import { decodeGlossaryState, decodeTranslationOrigin, decodeTranslationState } from "@/utils/wire";
import { apiClient } from "./client";


interface RawChapterSummary {
    id: number;
    index: number;
    title: string;
    glossaryState: number | string;
    translationState: number | string;
}


interface RawChapter extends RawChapterSummary {
    sourceMarkdown: string;
    translations: {
        id: number;
        language: string;
        markdown: string;
        origin: number | string;
        costUsd: number | null;
        createdAt: string;
    }[];
}


function toSummary(raw: RawChapterSummary): ChapterSummary {
    return {
        id: raw.id,
        index: raw.index,
        title: raw.title,
        glossaryState: decodeGlossaryState(raw.glossaryState),
        translationState: decodeTranslationState(raw.translationState),
    };
}


export const chaptersApi = {
    list(novelId: number): Promise<ChapterSummary[]> {
        return apiClient<RawChapterSummary[]>(`/api/novels/${novelId}/chapters`).then(rows => rows.map(toSummary));
    },

    getById(novelId: number, chapterId: number): Promise<Chapter> {
        return apiClient<RawChapter>(`/api/novels/${novelId}/chapters/${chapterId}`).then(raw => ({
            ...toSummary(raw),
            sourceMarkdown: raw.sourceMarkdown,
            translations: raw.translations.map(translation => ({
                id: translation.id,
                language: translation.language,
                markdown: translation.markdown,
                origin: decodeTranslationOrigin(translation.origin),
                costUsd: translation.costUsd,
                createdAt: translation.createdAt,
            })),
        }));
    },

    remove(novelId: number, chapterId: number): Promise<void> {
        return apiClient<void>(`/api/novels/${novelId}/chapters/${chapterId}`, {
            method: "DELETE",
        });
    },

    import(novelId: number, chapters: ImportedChapter[]): Promise<ChapterSummary[]> {
        return apiClient<RawChapterSummary[]>(`/api/novels/${novelId}/chapters/import`, {
            method: "POST",
            body: chapters,
        }).then(rows => rows.map(toSummary));
    },
};
