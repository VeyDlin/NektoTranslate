import type { ImportedChapter } from "@/types/api/requests";

import type { Chapter, ChapterSummary, StatusMessage } from "@/types/models/domain";
import {
    decodeGlossaryState,
    decodeTranslationIssueState,
    decodeTranslationOrigin,
    decodeTranslationState,
} from "@/utils/wire";
import { apiClient } from "./client";


interface RawChapterSummary {
    id: number;
    index: number;
    title: string;
    glossaryState: number | string;
    translationState: number | string;
    hasOriginal: boolean;
    hasTranslation: boolean;
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
    issues: {
        id: number;
        language: string;
        check: string;
        message: StatusMessage;
        blockIndex: number | null;
        state: number | string;
    }[];
}


function toSummary(raw: RawChapterSummary): ChapterSummary {
    return {
        id: raw.id,
        index: raw.index,
        title: raw.title,
        glossaryState: decodeGlossaryState(raw.glossaryState),
        translationState: decodeTranslationState(raw.translationState),
        hasOriginal: raw.hasOriginal,
        hasTranslation: raw.hasTranslation,
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
            issues: raw.issues.map(issue => ({
                id: issue.id,
                language: issue.language,
                check: issue.check,
                message: issue.message,
                blockIndex: issue.blockIndex,
                state: decodeTranslationIssueState(issue.state),
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
