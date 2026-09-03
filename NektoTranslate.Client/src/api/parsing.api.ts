import type { ImportFromUrlResult, ParserSupport } from "@/types/api/requests";
import type { ParsedChapterLink } from "@/types/models/domain";

import { apiClient } from "./client";


export const parsingApi = {
    // Answered without visiting the site: which parser claims a URL depends only on its host name,
    // so the user learns whether a link is readable before anything is downloaded.
    support(url: string): Promise<ParserSupport> {
        return apiClient<ParserSupport>(`/api/parsing/support?url=${encodeURIComponent(url)}`);
    },

    tableOfContents(url: string): Promise<ParsedChapterLink[]> {
        return apiClient<ParsedChapterLink[]>("/api/parsing/table-of-contents", {
            method: "POST",
            body: { url },
        });
    },

    // Takes the chapters the user chose, not a contents URL. A novel with two thousand entries must
    // never be pulled down wholesale because someone pasted a link.
    import(novelId: number, chapters: ParsedChapterLink[]): Promise<ImportFromUrlResult> {
        return apiClient<ImportFromUrlResult>(`/api/parsing/novels/${novelId}/import`, {
            method: "POST",
            body: chapters,
        });
    },
};
