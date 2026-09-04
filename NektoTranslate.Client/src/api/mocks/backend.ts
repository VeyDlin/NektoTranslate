import type { ProsePair, Seed, SeedChapter } from "./seed";
import type { ImportedChapter, StartTranslationJobRequest, UpsertGlossaryEntryRequest } from "@/types/api/requests";
import type {
    ChatMessage,
    GlossaryEntry,
    JobState,
    ParsedChapterLink,
    TranslationJob,
    TranslationState,
} from "@/types/models/domain";

import { mockEmitter } from "./emitter";
import { CHINESE_PROSE, createSeed, IDEOGRAPHIC_SPACE, JAPANESE_PROSE } from "./seed";


// The server serialises enums as names on both transports now — it added JsonStringEnumConverter to
// stop the frontend translating between two representations of the same value — so the mock emits
// names too. The number branch in @/utils/wire stays as a guard against an older build, not because
// anything currently sends integers.


const CHAPTER_MS = 1400;
const REQUEST_LATENCY_MS = 180;


function hostOf(url: string): string {
    try {
        return new URL(url).host;
    }
    catch {
        return "";
    }
}


// A contents page for a book that is still being written: a few hundred entries, newest last, with
// the numbering the source site would carry rather than the numbering this application assigns.
function buildTableOfContents(url: string): { sourceUrl: string; title: string }[] {
    const host = hostOf(url);

    if (host === "") {
        return [];
    }

    const titles = ["灰と鉄", "炉の前で", "通行証", "名前を二度", "三日目の空", "錬金術師の区画"];

    return Array.from({ length: 240 }, (_, at) => ({
        sourceUrl: `${url.replace(/\/+$/, "")}/${at + 1}/`,
        title: `第${at + 1}話${IDEOGRAPHIC_SPACE}${titles[at % titles.length]}`,
    }));
}


function delay(ms: number): Promise<void> {
    return new Promise((resolve) => {
        setTimeout(resolve, ms);
    });
}


export class MockBackend {

    private seed: Seed = createSeed();

    private nextNovelId = 100;

    private nextChapterId = 900_000;

    private nextJobId = 100;

    private nextTranslationId = 900_000;

    private nextGlossaryId = 900;

    private nextChatId = 100;

    private cancelled = new Set<number>();


    public async handle(method: string, path: string, body: unknown): Promise<unknown> {
        await delay(REQUEST_LATENCY_MS);

        const [rawPath = "", rawQuery = ""] = path.split("?");
        const query = new URLSearchParams(rawQuery);
        const segments = rawPath.replace(/^\/+|\/+$/g, "").split("/");

        // /api/novels...
        if (segments[0] !== "api") {
            throw new MockNotFound(method, path);
        }


        if (segments[1] === "settings") {
            if (method === "GET") {
                return { ...this.seed.settings };
            }

            if (method === "PUT") {
                const request = body as Record<string, unknown> | undefined;

                // null means "leave alone" and an empty string clears — the same distinction the
                // server draws, so the screens are exercised against the real semantics. Only the
                // style guide is nullable, which is why it is handled apart from the two models.
                if (typeof request?.globalStyleGuide === "string") {
                    this.seed.settings.globalStyleGuide = request.globalStyleGuide === ""
                        ? null
                        : request.globalStyleGuide;
                }

                for (const field of ["defaultModel", "glossaryModel"] as const) {
                    const value = request?.[field];

                    if (typeof value === "string" && value !== "") {
                        this.seed.settings[field] = value;
                    }
                }

                this.seed.settings.updatedAt = new Date().toISOString();

                return { ...this.seed.settings };
            }
        }

        if (segments[1] === "parsing") {
            return this.handleParsing(method, segments, query, body, path);
        }

        if (segments[1] === "parsers") {
            return this.handleParsers(method, segments, body, path);
        }

        if (segments[1] !== "novels") {
            throw new MockNotFound(method, path);
        }

        if (segments.length === 2) {
            if (method === "GET") {
                return this.listNovels();
            }

            if (method === "POST") {
                return this.createNovel(body as Record<string, string>);
            }
        }

        const novelId = Number(segments[2]);

        if (Number.isNaN(novelId)) {
            throw new MockNotFound(method, path);
        }

        if (segments.length === 3 && method === "GET") {
            return this.getNovel(novelId);
        }

        if (segments.length === 3 && method === "PUT") {
            return this.updateNovel(novelId, body as Record<string, unknown>);
        }

        if (segments.length === 3 && method === "DELETE") {
            return this.deleteNovel(novelId);
        }

        if (segments[3] === "chapters") {
            if (segments[4] === "import" && method === "POST") {
                return this.importChapters(novelId, body as ImportedChapter[]);
            }

            if (segments.length === 4 && method === "GET") {
                return this.listChapters(novelId);
            }

            if (segments.length === 5 && method === "GET") {
                return this.getChapter(novelId, Number(segments[4]));
            }

            if (segments.length === 5 && method === "DELETE") {
                return this.deleteChapter(novelId, Number(segments[4]));
            }
        }

        if (segments[3] === "chat") {
            if (method === "GET") {
                return (this.seed.chat[novelId] ?? []).map(message => ({ ...message }));
            }

            if (method === "POST") {
                return this.sendChatMessage(novelId, (body as { text?: string } | undefined)?.text ?? "");
            }
        }

        if (segments[3] === "glossary") {
            if (segments.length === 4 && method === "GET") {
                return this.listGlossary(novelId, query.get("onlyNeedsReview") === "true");
            }

            if (segments.length === 4 && method === "PUT") {
                return this.upsertGlossaryEntry(novelId, body as UpsertGlossaryEntryRequest);
            }

            if (segments[5] === "approve" && method === "POST") {
                return this.approveGlossaryEntry(novelId, Number(segments[4]));
            }

            if (segments.length === 5 && method === "DELETE") {
                return this.deleteGlossaryEntry(novelId, Number(segments[4]));
            }
        }

        if (segments[3] === "jobs") {
            if (segments.length === 4 && method === "GET") {
                return this.listJobs(novelId);
            }

            if (segments.length === 4 && method === "POST") {
                return this.startJob(novelId, body as StartTranslationJobRequest);
            }

            if (segments[5] === "cancel" && method === "POST") {
                return this.cancelJob(Number(segments[4]));
            }
        }

        throw new MockNotFound(method, path);
    }


    // Support is decided from the host name alone, exactly as the server does it — nothing is
    // fetched to answer the question.
    private handleParsing(
        method: string,
        segments: string[],
        query: URLSearchParams,
        body: unknown,
        path: string,
    ): unknown {
        if (segments[2] === "support" && method === "GET") {
            const host = hostOf(query.get("url") ?? "");
            const parser = this.seed.parsers.find(candidate => candidate.hostName === host && candidate.enabled);

            return { supported: parser !== undefined, parser: parser?.hostName ?? null };
        }

        if (segments[2] === "table-of-contents" && method === "POST") {
            return buildTableOfContents((body as { url?: string } | undefined)?.url ?? "");
        }

        if (segments[2] === "novels" && segments[4] === "import" && method === "POST") {
            const chosen = (body as ParsedChapterLink[] | undefined) ?? [];

            return this.importFromUrl(Number(segments[3]), chosen);
        }

        throw new MockNotFound(method, path);
    }


    private handleParsers(method: string, segments: string[], body: unknown, path: string): unknown {
        if (segments.length === 2 && method === "GET") {
            return this.seed.parsers.map(parser => ({ ...parser }));
        }

        const hostName = decodeURIComponent(segments[2] ?? "");

        if (segments[3] === "source" && method === "GET") {
            return this.seed.parserSources[hostName]
                ?? `// No script stored for ${hostName}. Saving one here creates an override.\n`;
        }

        if (segments.length === 3 && method === "PUT") {
            const request = body as { scriptSource?: string; enabled?: boolean } | undefined;
            const parser = this.seed.parsers.find(candidate => candidate.hostName === hostName);

            this.seed.parserSources[hostName] = request?.scriptSource ?? "";

            if (parser === undefined) {
                this.seed.parsers.push({ hostName, bundled: false, edited: true, enabled: request?.enabled ?? true });
            }
            else {
                parser.edited = true;
                parser.enabled = request?.enabled ?? parser.enabled;
            }

            return { hostName, scriptSource: request?.scriptSource ?? "" };
        }

        // Reverting drops the override. A bundled parser comes back because it was only shadowed;
        // one the user added disappears entirely, because there is nothing underneath it.
        if (segments.length === 3 && method === "DELETE") {
            const at = this.seed.parsers.findIndex(candidate => candidate.hostName === hostName);

            if (at < 0) {
                throw new MockNotFound(method, path);
            }

            delete this.seed.parserSources[hostName];

            if (this.seed.parsers[at]!.bundled) {
                this.seed.parsers[at]!.edited = false;
                this.seed.parsers[at]!.enabled = true;
            }
            else {
                this.seed.parsers.splice(at, 1);
            }

            return null;
        }

        throw new MockNotFound(method, path);
    }


    private importFromUrl(novelId: number, chosen: ParsedChapterLink[]): unknown {
        const existing = this.chaptersOf(novelId);
        let nextIndex = existing.reduce((highest, chapter) => Math.max(highest, chapter.index), 0);
        const failures: string[] = [];

        chosen.forEach((link, at) => {
            // One in nine refuses, so the partial-success path is something the interface has to
            // have shown before it meets a real site.
            if (at % 9 === 8) {
                failures.push(`${link.title} — the page did not answer in time`);
                return;
            }

            this.nextChapterId += 1;
            nextIndex += 1;

            this.seed.chapters.push({
                id: this.nextChapterId,
                novelId,
                index: nextIndex,
                title: link.title,
                sourceHeading: link.title,
                translatedHeading: link.title,
                proseIndex: at % JAPANESE_PROSE.length,
                glossaryState: "NotAnalyzed",
                translationState: "None",
                translations: [],
            });
        });

        return { imported: chosen.length - failures.length, failures };
    }


    private sendChatMessage(novelId: number, text: string): unknown {
        const thread = this.seed.chat[novelId] ?? [];
        const now = new Date().toISOString();

        this.nextChatId += 1;
        const asked: ChatMessage = { id: this.nextChatId, role: "User", text, costUsd: 0, createdAt: now };

        this.nextChatId += 1;
        const tool: ChatMessage = {
            id: this.nextChatId,
            role: "Tool",
            text: `glossary_search — looked for terms mentioned in "${text.slice(0, 40)}"`,
            costUsd: 0,
            createdAt: now,
        };

        this.nextChatId += 1;
        const replied: ChatMessage = {
            id: this.nextChatId,
            role: "Agent",
            text: "There is no model behind the mock, so this is a placeholder rather than an answer. "
                + "The turn is shaped the way a real one would be: what you asked, what the agent ran, "
                + "and what it said.",
            costUsd: 0.011,
            createdAt: now,
        };

        this.seed.chat[novelId] = [...thread, asked, tool, replied];

        return [asked, tool, replied].map(message => ({ ...message }));
    }


    private listNovels(): unknown[] {
        return [...this.seed.novels]
            .sort((left, right) => right.createdAt.localeCompare(left.createdAt))
            .map(novel => ({ ...novel }));
    }


    private getNovel(novelId: number): unknown {
        const novel = this.seed.novels.find(candidate => candidate.id === novelId);

        if (novel === undefined) {
            throw new MockNotFound("GET", `/api/novels/${novelId}`);
        }

        return { ...novel };
    }


    private createNovel(request: Record<string, string>): unknown {
        this.nextNovelId += 1;

        const novel = {
            id: this.nextNovelId,
            title: request.title ?? "Untitled",
            sourceLanguage: request.sourceLanguage ?? "",
            targetLanguage: request.targetLanguage ?? "",
            sourceUrl: request.sourceUrl || null,
            styleGuide: request.styleGuide || null,
            model: "sonnet",
            normalizeQuotes: true,
            createdAt: new Date().toISOString(),
        };

        this.seed.novels.push(novel);

        return { ...novel };
    }


    private updateNovel(novelId: number, request: Record<string, unknown>): unknown {
        const novel = this.seed.novels.find(candidate => candidate.id === novelId);

        if (novel === undefined) {
            throw new MockNotFound("PUT", `/api/novels/${novelId}`);
        }

        for (const field of ["title", "sourceLanguage", "targetLanguage", "model"] as const) {
            const value = request[field];

            if (typeof value === "string" && value !== "") {
                novel[field] = value;
            }
        }

        // These two are nullable, so an empty string clears them rather than being ignored.
        for (const field of ["sourceUrl", "styleGuide"] as const) {
            const value = request[field];

            if (typeof value === "string") {
                novel[field] = value === "" ? null : value;
            }
        }

        if (typeof request.normalizeQuotes === "boolean") {
            novel.normalizeQuotes = request.normalizeQuotes;
        }

        return { ...novel };
    }


    // Cascades the way the server does: nothing about the book is left behind to reappear in a list
    // whose novel no longer exists.
    private deleteNovel(novelId: number): unknown {
        const at = this.seed.novels.findIndex(novel => novel.id === novelId);

        if (at < 0) {
            throw new MockNotFound("DELETE", `/api/novels/${novelId}`);
        }

        this.seed.novels.splice(at, 1);
        this.seed.chapters = this.seed.chapters.filter(chapter => chapter.novelId !== novelId);
        this.seed.glossary = this.seed.glossary.filter(entry => entry.novelId !== novelId);
        this.seed.jobs = this.seed.jobs.filter(job => job.novelId !== novelId);
        delete this.seed.chat[novelId];

        return null;
    }


    private deleteChapter(novelId: number, chapterId: number): unknown {
        const at = this.seed.chapters.findIndex(
            chapter => chapter.novelId === novelId && chapter.id === chapterId,
        );

        if (at < 0) {
            throw new MockNotFound("DELETE", `/api/novels/${novelId}/chapters/${chapterId}`);
        }

        this.seed.chapters.splice(at, 1);

        return null;
    }


    private chaptersOf(novelId: number): SeedChapter[] {
        return this.seed.chapters.filter(chapter => chapter.novelId === novelId);
    }


    // Mirrors the controller's projection exactly: no bodies, no translations. The list is the one
    // request a two-thousand-chapter novel makes constantly, so it stays this thin on purpose.
    private listChapters(novelId: number): unknown[] {
        return this.chaptersOf(novelId).map(chapter => ({
            id: chapter.id,
            index: chapter.index,
            title: chapter.title,
            glossaryState: chapter.glossaryState,
            translationState: chapter.translationState,
        }));
    }


    private proseFor(chapter: SeedChapter): ProsePair {
        const pool = chapter.novelId === 2 ? CHINESE_PROSE : JAPANESE_PROSE;

        return pool[chapter.proseIndex % pool.length]!;
    }


    private getChapter(novelId: number, chapterId: number): unknown {
        const chapter = this.chaptersOf(novelId).find(candidate => candidate.id === chapterId);

        if (chapter === undefined) {
            throw new MockNotFound("GET", `/api/novels/${novelId}/chapters/${chapterId}`);
        }

        const prose = this.proseFor(chapter);

        return {
            id: chapter.id,
            index: chapter.index,
            title: chapter.title,
            sourceMarkdown: `## ${chapter.sourceHeading}\n\n${prose.sourceMarkdown}`,
            glossaryState: chapter.glossaryState,
            translationState: chapter.translationState,
            translations: [...chapter.translations]
                .sort((left, right) => right.createdAt.localeCompare(left.createdAt))
                .map(translation => ({
                    id: translation.id,
                    language: translation.language,
                    markdown: `## ${chapter.translatedHeading}\n\n${prose.translationMarkdown}`,
                    origin: translation.origin,
                    costUsd: translation.costUsd,
                    createdAt: translation.createdAt,
                })),

            // No checks run against the mock, so there is never anything to report. The field is
            // still sent because the reader reads it at render time, and a list that arrives
            // missing is not the same thing as one that arrives empty.
            issues: [],
        };
    }


    private importChapters(novelId: number, chapters: ImportedChapter[]): unknown[] {
        const existing = this.chaptersOf(novelId);
        let nextIndex = existing.reduce((highest, chapter) => Math.max(highest, chapter.index), 0);
        const created: SeedChapter[] = [];

        for (const incoming of chapters) {
            this.nextChapterId += 1;
            nextIndex += 1;

            const chapter: SeedChapter = {
                id: this.nextChapterId,
                novelId,
                index: incoming.index ?? nextIndex,
                title: incoming.title,
                sourceHeading: incoming.title,
                translatedHeading: incoming.title,
                proseIndex: 0,
                glossaryState: "NotAnalyzed",
                translationState: "None",
                translations: [],
            };

            this.seed.chapters.push(chapter);
            created.push(chapter);
        }

        return created.map(chapter => ({
            id: chapter.id,
            index: chapter.index,
            title: chapter.title,
            glossaryState: chapter.glossaryState,
            translationState: chapter.translationState,
        }));
    }


    // Longest source term first, matching the server's own ordering. 田中 sits inside 田中太郎, and
    // filing the short one above the long one makes the pair look like one entry with a typo.
    private listGlossary(novelId: number, onlyNeedsReview: boolean): unknown[] {
        return this.seed.glossary
            .filter(entry => entry.novelId === novelId && (!onlyNeedsReview || entry.needsReview))
            .sort((left, right) => (
                right.sourceTerm.length - left.sourceTerm.length
                || left.sourceTerm.localeCompare(right.sourceTerm)
            ))
            .map(entry => ({ ...entry }));
    }


    private upsertGlossaryEntry(novelId: number, request: UpsertGlossaryEntryRequest): unknown {
        const existing = this.seed.glossary.find(entry => (
            entry.novelId === novelId
            && entry.language === request.language
            && entry.sourceTerm === request.sourceTerm
        ));

        if (existing !== undefined) {
            existing.targetTerm = request.targetTerm;
            existing.category = request.category ?? existing.category;
            existing.notes = request.notes ?? existing.notes;
            existing.aliases = request.aliases ?? existing.aliases;

            // A hand edit is a hand edit: the rendering now comes from the user, and it outranks
            // whatever the model or the book's own earlier translation had chosen.
            existing.origin = "Manual";
            existing.needsReview = false;
            existing.updatedAt = new Date().toISOString();

            return { ...existing };
        }

        this.nextGlossaryId += 1;

        const created: GlossaryEntry = {
            id: this.nextGlossaryId,
            novelId,
            language: request.language,
            sourceTerm: request.sourceTerm,
            targetTerm: request.targetTerm,
            category: request.category ?? "Other",
            aliases: request.aliases ?? [],
            notes: request.notes ?? null,
            origin: "Manual",
            confidence: 1,
            needsReview: false,
            firstSeenChapterId: null,
            updatedAt: new Date().toISOString(),
        };

        this.seed.glossary.push(created);

        return { ...created };
    }


    private approveGlossaryEntry(novelId: number, entryId: number): unknown {
        const entry = this.seed.glossary.find(candidate => candidate.novelId === novelId && candidate.id === entryId);

        if (entry === undefined) {
            throw new MockNotFound("POST", `/api/novels/${novelId}/glossary/${entryId}/approve`);
        }

        // Only the flag. The origin stays as it was, because approving does not change where the
        // rendering came from.
        entry.needsReview = false;

        return null;
    }


    private deleteGlossaryEntry(novelId: number, entryId: number): unknown {
        const at = this.seed.glossary.findIndex(entry => entry.novelId === novelId && entry.id === entryId);

        if (at < 0) {
            throw new MockNotFound("DELETE", `/api/novels/${novelId}/glossary/${entryId}`);
        }

        this.seed.glossary.splice(at, 1);

        return null;
    }


    private listJobs(novelId: number): unknown[] {
        return this.seed.jobs
            .filter(job => job.novelId === novelId)
            .sort((left, right) => right.createdAt.localeCompare(left.createdAt))
            .map(job => this.serializeJob(job));
    }


    private serializeJob(job: TranslationJob): unknown {
        return {
            ...job,
            scopeKind: job.scopeKind,
            state: job.state,
        };
    }


    // Chapters that are already translated drop out when the scope is resolved, so `totalCount` is
    // the number that will actually be worked on rather than the size of the range the user picked.
    private resolveScope(novelId: number, request: StartTranslationJobRequest): SeedChapter[] {
        const all = this.chaptersOf(novelId);

        const inScope = all.filter((chapter) => {
            switch (request.scopeKind) {
                case "Range":
                    return chapter.index >= (request.fromIndex ?? 1)
                        && chapter.index <= (request.toIndex ?? Number.MAX_SAFE_INTEGER);
                case "Single":
                case "Selection":
                    return (request.chapterIds ?? []).includes(chapter.id);
                default:
                    return true;
            }
        });

        // `force` keeps the chapters that are already done. On the real server their cached batches
        // come back unchanged unless the instructions behind them changed, so this is a re-run
        // against current settings rather than a fresh translation.
        return request.force === true
            ? inScope
            : inScope.filter(chapter => chapter.translationState !== "Translated");
    }


    private startJob(novelId: number, request: StartTranslationJobRequest): unknown {
        const targets = this.resolveScope(novelId, request);

        this.nextJobId += 1;

        const job: TranslationJob = {
            id: this.nextJobId,
            novelId,
            scopeKind: request.scopeKind,
            fromIndex: request.fromIndex ?? null,
            toIndex: request.toIndex ?? null,
            chapterIds: request.chapterIds ?? [],
            state: "Queued",
            processedCount: 0,
            totalCount: targets.length,
            costUsd: 0,
            budgetUsd: request.budgetUsd ?? null,
            createdAt: new Date().toISOString(),
            startedAt: null,
            finishedAt: null,
            error: null,
        };

        this.seed.jobs.push(job);

        for (const chapter of targets) {
            chapter.translationState = "Queued";
            this.publishChapterState(novelId, chapter);
        }

        void this.run(job, targets);

        return this.serializeJob(job);
    }


    private cancelJob(jobId: number): unknown {
        const job = this.seed.jobs.find(candidate => candidate.id === jobId);

        if (job === undefined || job.state !== "Running") {
            throw new MockNotFound("POST", `/api/novels/jobs/${jobId}/cancel`);
        }

        this.cancelled.add(jobId);

        return { accepted: true };
    }


    private publishChapterState(novelId: number, chapter: SeedChapter): void {
        mockEmitter.emit(novelId, "ChapterStateChanged", {
            chapterId: chapter.id,
            state: chapter.translationState,
        });
    }


    private publishJobState(job: TranslationJob): void {
        mockEmitter.emit(job.novelId, "JobStateChanged", {
            jobId: job.id,
            state: job.state,
            processed: job.processedCount,
            total: job.totalCount,
            costUsd: job.costUsd,
        });
    }


    private settle(job: TranslationJob, state: JobState, message: string): void {
        job.state = state;
        job.finishedAt = new Date().toISOString();
        this.publishJobState(job);
        mockEmitter.emit(job.novelId, "AgentMessage", { message });
    }


    // Chapters are committed one at a time, in order, because chapter N+1 is translated with the
    // glossary as chapter N left it. Nothing here runs in parallel, and the interface must not
    // suggest it could.
    private async run(job: TranslationJob, targets: SeedChapter[]): Promise<void> {
        job.state = "Running";
        job.startedAt = new Date().toISOString();
        this.publishJobState(job);

        for (const chapter of targets) {
            if (this.cancelled.has(job.id)) {
                this.cancelled.delete(job.id);
                this.resetQueued(targets);
                this.settle(job, "Cancelled", "Run cancelled. The chapter already in flight was finished and kept.");
                return;
            }

            if (job.budgetUsd !== null && job.costUsd >= job.budgetUsd) {
                this.resetQueued(targets);
                this.settle(
                    job,
                    "Paused",
                    `Paused at the ${job.budgetUsd.toFixed(2)} spending ceiling, ${job.processedCount} of ${job.totalCount} chapters done.`,
                );
                return;
            }

            chapter.translationState = "Running";
            this.publishChapterState(job.novelId, chapter);

            await this.streamChapter(job.novelId, chapter);

            this.nextTranslationId += 1;

            const cost = 0.028 + (chapter.index % 6) * 0.004;

            chapter.translations.push({
                id: this.nextTranslationId,
                language: "English",
                markdown: "",
                origin: "Ai",
                costUsd: cost,
                createdAt: new Date().toISOString(),
            });

            chapter.translationState = "Translated";
            chapter.glossaryState = "Analyzed";
            job.processedCount += 1;
            job.costUsd = Number((job.costUsd + cost).toFixed(4));

            this.publishChapterState(job.novelId, chapter);
            mockEmitter.emit(job.novelId, "ChapterTranslated", { chapterId: chapter.id });
            this.publishJobState(job);

            if (chapter.index % 4 === 0) {
                mockEmitter.emit(job.novelId, "GlossaryChanged", {
                    sourceTerm: "灰降ろし",
                    targetTerm: "ashfall rite",
                    origin: "AiExtracted",
                });
            }
        }

        this.settle(job, "Completed", `Finished ${job.processedCount} chapters for ${job.costUsd.toFixed(2)}.`);
    }


    // Writes the chapter out in pieces rather than sleeping and producing it whole. A run over nine
    // hundred chapters spends most of its life on one chapter, and watching that chapter appear is
    // the difference between a working application and a spinner.
    private async streamChapter(novelId: number, chapter: SeedChapter): Promise<void> {
        const prose = this.proseFor(chapter);
        const full = `## ${chapter.translatedHeading}\n\n${prose.translationMarkdown}`;
        const pieces = full.match(/[\s\S]{1,24}/g) ?? [];
        const pause = Math.max(20, Math.round(CHAPTER_MS / Math.max(pieces.length, 1)));

        for (const piece of pieces) {
            mockEmitter.emit(novelId, "TranslationDelta", { chapterId: chapter.id, text: piece });
            await delay(pause);
        }
    }


    private resetQueued(targets: SeedChapter[]): void {
        for (const chapter of targets) {
            if (chapter.translationState === "Queued") {
                chapter.translationState = "None";
                this.publishChapterState(chapter.novelId, chapter);
            }
        }
    }
}


export class MockNotFound extends Error {

    public status = 404;


    public constructor(method: string, path: string) {
        super(`Mock backend has no route for ${method} ${path}`);
        this.name = "MockNotFound";
    }
}


export const mockBackend = new MockBackend();


export type { TranslationState };
