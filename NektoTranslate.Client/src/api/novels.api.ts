import type { CreateNovelRequest, UpdateNovelRequest } from "@/types/api/requests";

import type { Novel, NovelListItem } from "@/types/models/domain";
import { apiClient } from "./client";


export const novelsApi = {
    list(): Promise<NovelListItem[]> {
        return apiClient<NovelListItem[]>("/api/novels");
    },

    getById(novelId: number): Promise<Novel> {
        return apiClient<Novel>(`/api/novels/${novelId}`);
    },

    create(data: CreateNovelRequest): Promise<Novel> {
        return apiClient<Novel>("/api/novels", {
            method: "POST",
            body: data,
        });
    },

    update(novelId: number, data: UpdateNovelRequest): Promise<Novel> {
        return apiClient<Novel>(`/api/novels/${novelId}`, {
            method: "PUT",
            body: data,
        });
    },

    // Cascades on the server to chapters, translations, glossary, chat and jobs. Everything about
    // the book goes, which is why the interface asks before calling it.
    remove(novelId: number): Promise<void> {
        return apiClient<void>(`/api/novels/${novelId}`, {
            method: "DELETE",
        });
    },
};
