import type {
    AppSettings,
    ChapterTranslation,
    ChatMessage,
    GlossaryEntry,
    GlossaryState,
    ImportJob,
    Novel,
    ParserScriptSummary,
    TranslationJob,
    TranslationState,
} from "@/types/models/domain";


// Bodies are composed on demand from these pairs rather than stored per chapter. Two and a half
// thousand chapters of stored prose would be a pointless megabyte in memory, and the real API does
// not hand out bodies in a list either.
export interface ProsePair {
    sourceMarkdown: string;
    translationMarkdown: string;
}


// Ruby survives the server's sanitizer allowlist, so the reader has to render it. It is here in the
// fixtures for the same reason: a furigana bug is invisible against prose that never uses any.
export const JAPANESE_PROSE: ProsePair[] = [
    {
        sourceMarkdown: [
            "朝の<ruby>光<rt>ひかり</rt></ruby>が、まだ冷たい<ruby>鉄<rt>てつ</rt></ruby>の匂いを運んできた。",
            "「起きているのか」",
            "声は<ruby>炉<rt>ろ</rt></ruby>の向こうから聞こえた。ミナは答えなかった。答えれば、昨日の続きが始まってしまう。",
            "灰は三日降りつづけている。<ruby>街<rt>まち</rt></ruby>の者はもう空を見上げない。見上げたところで、何も変わらないと知ってしまったからだ。",
        ].join("\n\n"),
        translationMarkdown: [
            "Morning light came in carrying the smell of iron, still cold from the night.",
            "«So you're awake.»",
            "The voice came from the far side of the furnace. Mina did not answer. To answer would be to start yesterday over again.",
            "The ash had been falling for three days. Nobody in town looked up at the sky any more. They had learned that looking changed nothing.",
        ].join("\n\n"),
    },
    {
        sourceMarkdown: [
            "<ruby>田中太郎<rt>たなかたろう</rt></ruby>は門の前で足を止めた。",
            "「この先は<ruby>錬金術師<rt>れんきんじゅつし</rt></ruby>の区画だ。通行証は」",
            "衛兵の声には敵意はなかった。ただ、疲れていた。",
            "彼は懐から一枚の<ruby>札<rt>ふだ</rt></ruby>を取り出した。三年前の日付が入っている。それでも衛兵は何も言わずに道を空けた。",
        ].join("\n\n"),
        translationMarkdown: [
            "Tanaka Tarou stopped in front of the gate.",
            "«Past this point is the alchemists' quarter. Your pass.»",
            "There was no hostility in the guard's voice. Only tiredness.",
            "He drew a slip of paper from inside his coat. The date on it was three years old. The guard stood aside without a word.",
        ].join("\n\n"),
    },
    {
        sourceMarkdown: [
            "<ruby>灰<rt>はい</rt></ruby>の街には、名前を二度言ってはいけないという決まりがある。",
            "一度目は呼ぶため。二度目は<ruby>縛<rt>しば</rt></ruby>るため。",
            "ミナはその決まりを守らなかった。だから今、彼女の名前は彼女のものではない。",
        ].join("\n\n"),
        translationMarkdown: [
            "In Ash Town there is a rule that a name must never be spoken twice.",
            "The first time calls. The second time binds.",
            "Mina did not keep the rule. That is why her name no longer belongs to her.",
        ].join("\n\n"),
    },
];


export const CHINESE_PROSE: ProsePair[] = [
    {
        sourceMarkdown: [
            "長夜第一年，劍宗封山。",
            "「你當真要走？」",
            "沈硯沒有回頭。山門外的雪已經積到膝蓋，再等一日，路就沒了。",
        ].join("\n\n"),
        translationMarkdown: [
            "In the first year of the Long Night, the Sword Sect sealed the mountain.",
            "«You truly mean to go?»",
            "Shen Yan did not look back. Outside the gate the snow was already knee-deep. One more day and there would be no road at all.",
        ].join("\n\n"),
    },
    {
        sourceMarkdown: [
            "他在山下住了七年，沒有再碰過劍。",
            "鄰人只當他是個沉默的鐵匠。直到那年秋天，有人帶著一柄斷劍找上門來。",
        ].join("\n\n"),
        translationMarkdown: [
            "He lived below the mountain for seven years and never touched a sword again.",
            "His neighbours took him for a quiet blacksmith. Then, that autumn, someone came to his door carrying a broken blade.",
        ].join("\n\n"),
    },
];


const JAPANESE_TITLES: [string, string][] = [
    ["灰と鉄", "Ash and Iron"],
    ["炉の前で", "Before the Furnace"],
    ["通行証", "The Pass"],
    ["名前を二度", "A Name Spoken Twice"],
    ["三日目の空", "The Sky on the Third Day"],
    ["錬金術師の区画", "The Alchemists' Quarter"],
    ["夜明けの取引", "A Bargain at Daybreak"],
    ["帰らない者たち", "Those Who Do Not Return"],
];


const CHINESE_TITLES: [string, string][] = [
    ["封山", "Sealing the Mountain"],
    ["斷劍", "The Broken Blade"],
    ["鐵匠", "The Blacksmith"],
    ["雪中行", "Walking in Snow"],
    ["第七年", "The Seventh Year"],
    ["長夜之始", "The Long Night Begins"],
];


export interface SeedChapter {
    id: number;
    novelId: number;
    index: number;
    title: string;

    // The stored title is the title alone; the number belongs to the table of contents, which
    // already has a column for it. The headings below are what the chapter body opens with.
    sourceHeading: string;
    translatedHeading: string;
    proseIndex: number;
    glossaryState: GlossaryState;
    translationState: TranslationState;
    translations: ChapterTranslation[];
}


export interface Seed {
    novels: Novel[];
    chapters: SeedChapter[];
    glossary: GlossaryEntry[];
    jobs: TranslationJob[];
    imports: ImportJob[];
    settings: AppSettings;
    parsers: ParserScriptSummary[];
    parserSources: Record<string, string>;
    chat: Record<number, ChatMessage[]>;
}


// A handful of the four hundred parsers the server ships, enough to show every state the list can be
// in: bundled and untouched, bundled but edited, switched off, and one the user added themselves.
const PARSERS: ParserScriptSummary[] = [
    { hostName: "ncode.syosetu.com", bundled: true, edited: false, enabled: true },
    { hostName: "kakuyomu.jp", bundled: true, edited: false, enabled: true },
    { hostName: "novel18.syosetu.com", bundled: true, edited: false, enabled: false },
    { hostName: "www.qidian.com", bundled: true, edited: true, enabled: true },
    { hostName: "booktoki.example", bundled: false, edited: true, enabled: true },
];


const SAMPLE_PARSER_SOURCE = [
    "// Parser for ncode.syosetu.com",
    "//",
    "// Returns the chapter list from a table-of-contents page and the body of a single chapter.",
    "// Selectors only — nothing here should reach outside the document it is given.",
    "",
    "export function chapterList(document) {",
    "    return [...document.querySelectorAll(\".index_box .novel_sublist2 a\")].map(link => ({",
    "        sourceUrl: link.href,",
    "        title: link.textContent.trim(),",
    "    }));",
    "}",
    "",
    "export function chapterBody(document) {",
    "    return {",
    "        title: document.querySelector(\".novel_subtitle\")?.textContent.trim() ?? \"\",",
    "        html: document.querySelector(\"#novel_honbun\")?.innerHTML ?? \"\",",
    "    };",
    "}",
].join("\n");


// U+3000. A Latin space between a chapter number and its title sets far too tight against
// full-width characters, and the ideographic space is what Japanese and Chinese typography uses.
// Named rather than typed inline so it cannot be mistaken for a stray invisible character.
export const IDEOGRAPHIC_SPACE = String.fromCharCode(0x3000);


function daysAgo(days: number): string {
    return new Date(Date.now() - days * 86_400_000).toISOString();
}


// Novel 1 reproduces the abandoned-translation case from the product brief: a human rendered the
// first stretch and stopped, and the agent has been continuing from there. That is what makes the
// glossary's `FromExistingTranslation` origin worth showing — the names come from the book itself.
const ASH_TOWN_HUMAN_CHAPTERS = 120;
const ASH_TOWN_TRANSLATED = 186;
const ASH_TOWN_FAILED = 187;
const ASH_TOWN_TOTAL = 412;

const LONG_NIGHT_TRANSLATED = 24;
const LONG_NIGHT_TOTAL = 2003;


function buildChapters(
    novelId: number,
    total: number,
    titles: [string, string][],
    proseCount: number,
    startId: number,
    chapterWord: string,
    describe: (index: number) => { glossaryState: GlossaryState; translationState: TranslationState },
): SeedChapter[] {
    const chapters: SeedChapter[] = [];

    for (let index = 1; index <= total; index += 1) {
        const [title, translatedTitle] = titles[(index - 1) % titles.length]!;
        const state = describe(index);

        chapters.push({
            id: startId + index,
            novelId,
            index,
            title,
            sourceHeading: `第${index}${chapterWord}${IDEOGRAPHIC_SPACE}${title}`,
            translatedHeading: `Chapter ${index} — ${translatedTitle}`,
            proseIndex: (index - 1) % proseCount,
            glossaryState: state.glossaryState,
            translationState: state.translationState,
            translations: [],
        });
    }

    return chapters;
}


export function createSeed(): Seed {
    const novels: Novel[] = [
        {
            id: 1,
            title: "灰の街の錬金術師",
            sourceLanguage: "Japanese",
            targetLanguage: "English",
            sourceUrl: "https://ncode.example.jp/n4823hy/",
            styleGuide: "Keep honorifics as transliterations. Mina's register is blunt; do not soften it.",
            model: "sonnet",
            normalizeQuotes: true,
            createdAt: daysAgo(31),
        },
        {
            id: 2,
            title: "劍與長夜",
            sourceLanguage: "Chinese",
            targetLanguage: "English",
            sourceUrl: null,
            styleGuide: null,
            model: "sonnet",
            normalizeQuotes: true,
            createdAt: daysAgo(9),
        },
        {
            id: 3,
            title: "그림자 서점",
            sourceLanguage: "Korean",
            targetLanguage: "English",
            sourceUrl: null,
            styleGuide: null,
            model: "sonnet",
            normalizeQuotes: true,
            createdAt: daysAgo(1),
        },
    ];

    const ashTown = buildChapters(1, ASH_TOWN_TOTAL, JAPANESE_TITLES, JAPANESE_PROSE.length, 1000, "話", (index) => {
        if (index <= ASH_TOWN_TRANSLATED) {
            return { glossaryState: "Analyzed", translationState: "Translated" };
        }

        if (index === ASH_TOWN_FAILED) {
            return { glossaryState: "Analyzed", translationState: "Failed" };
        }

        return { glossaryState: "NotAnalyzed", translationState: "None" };
    });

    for (const chapter of ashTown) {
        if (chapter.translationState !== "Translated") {
            continue;
        }

        const human = chapter.index <= ASH_TOWN_HUMAN_CHAPTERS;

        chapter.translations.push({
            id: 50_000 + chapter.id,
            language: "English",
            markdown: "",
            origin: human ? "Imported" : "Ai",
            costUsd: human ? null : 0.031 + (chapter.index % 7) * 0.004,
            createdAt: daysAgo(human ? 30 : 6 - (chapter.index % 5)),
        });
    }

    // Chapter 12 carries both an inherited human rendering and a later machine pass. The reader has
    // to let the user pick between them; showing only the newest would silently bury the version
    // the reader has already read 120 chapters of.
    const contested = ashTown.find(chapter => chapter.index === 12);

    contested?.translations.push({
        id: 59_999,
        language: "English",
        markdown: "",
        origin: "Ai",
        costUsd: 0.041,
        createdAt: daysAgo(2),
    });

    const longNight = buildChapters(2, LONG_NIGHT_TOTAL, CHINESE_TITLES, CHINESE_PROSE.length, 20_000, "章", index => (
        index <= LONG_NIGHT_TRANSLATED
            ? { glossaryState: "Analyzed", translationState: "Translated" }
            : { glossaryState: "NotAnalyzed", translationState: "None" }
    ));

    for (const chapter of longNight) {
        if (chapter.translationState === "Translated") {
            chapter.translations.push({
                id: 70_000 + chapter.id,
                language: "English",
                markdown: "",
                origin: "Ai",
                costUsd: 0.028 + (chapter.index % 5) * 0.003,
                createdAt: daysAgo(8 - Math.floor(chapter.index / 4)),
            });
        }
    }

    const glossary: GlossaryEntry[] = [
        {
            id: 1,
            novelId: 1,
            language: "English",
            sourceTerm: "田中太郎",
            targetTerm: "Tanaka Tarou",
            category: "Person",
            aliases: ["太郎", "タロウ"],
            notes: "Male. Addressed by surname by everyone except Mina.",
            origin: "FromExistingTranslation",
            confidence: 1,
            needsReview: false,
            firstSeenChapterId: 1003,
            updatedAt: daysAgo(30),
        },
        {
            id: 2,
            novelId: 1,
            language: "English",
            sourceTerm: "田中",
            targetTerm: "Tanaka",
            category: "Person",
            aliases: [],
            notes: "The surname alone. Distinct entry so it does not swallow the full name.",
            origin: "FromExistingTranslation",
            confidence: 1,
            needsReview: false,
            firstSeenChapterId: 1003,
            updatedAt: daysAgo(30),
        },
        {
            id: 3,
            novelId: 1,
            language: "English",
            sourceTerm: "ミナ",
            targetTerm: "Mina",
            category: "Person",
            aliases: [],
            notes: "Female. Speaks bluntly, no honorifics to anyone.",
            origin: "FromExistingTranslation",
            confidence: 1,
            needsReview: false,
            firstSeenChapterId: 1001,
            updatedAt: daysAgo(30),
        },
        {
            id: 4,
            novelId: 1,
            language: "English",
            sourceTerm: "灰の街",
            targetTerm: "Ash Town",
            category: "Place",
            aliases: ["灰街"],
            notes: null,
            origin: "FromExistingTranslation",
            confidence: 1,
            needsReview: false,
            firstSeenChapterId: 1001,
            updatedAt: daysAgo(30),
        },
        {
            id: 5,
            novelId: 1,
            language: "English",
            sourceTerm: "錬金術師",
            targetTerm: "alchemist",
            category: "Other",
            aliases: [],
            notes: "Common noun, lowercase except in the quarter's name.",
            origin: "FromExistingTranslation",
            confidence: 1,
            needsReview: false,
            firstSeenChapterId: 1003,
            updatedAt: daysAgo(30),
        },
        {
            id: 6,
            novelId: 1,
            language: "English",
            sourceTerm: "第七工房",
            targetTerm: "the Seventh Workshop",
            category: "Organization",
            aliases: [],
            notes: null,
            origin: "AiExtracted",
            confidence: 0.82,
            needsReview: true,
            firstSeenChapterId: 1131,
            updatedAt: daysAgo(4),
        },
        {
            id: 7,
            novelId: 1,
            language: "English",
            sourceTerm: "灰降ろし",
            targetTerm: "ashfall rite",
            category: "Technique",
            aliases: ["灰おろし"],
            notes: "No established rendering existed. Invented — confirm before it spreads.",
            origin: "AiExtracted",
            confidence: 0.61,
            needsReview: true,
            firstSeenChapterId: 1145,
            updatedAt: daysAgo(3),
        },
        {
            id: 8,
            novelId: 1,
            language: "English",
            sourceTerm: "銀の秤",
            targetTerm: "the Silver Scale",
            category: "Item",
            aliases: [],
            notes: null,
            origin: "AiExtracted",
            confidence: 0.74,
            needsReview: true,
            firstSeenChapterId: 1152,
            updatedAt: daysAgo(3),
        },
        {
            id: 9,
            novelId: 1,
            language: "English",
            sourceTerm: "炉番",
            targetTerm: "furnace-keeper",
            category: "Other",
            aliases: [],
            notes: "Chosen by hand over the model's «furnace watchman».",
            origin: "Manual",
            confidence: 1,
            needsReview: false,
            firstSeenChapterId: 1002,
            updatedAt: daysAgo(2),
        },
        {
            id: 10,
            novelId: 1,
            language: "English",
            sourceTerm: "紅の門",
            targetTerm: "the Crimson Gate",
            category: "Place",
            aliases: [],
            notes: null,
            origin: "AiExtracted",
            confidence: 0.88,
            needsReview: false,
            firstSeenChapterId: 1160,
            updatedAt: daysAgo(2),
        },
        {
            id: 11,
            novelId: 2,
            language: "English",
            sourceTerm: "沈硯",
            targetTerm: "Shen Yan",
            category: "Person",
            aliases: [],
            notes: "Male. Given name used only by his master.",
            origin: "AiExtracted",
            confidence: 0.93,
            needsReview: false,
            firstSeenChapterId: 20_001,
            updatedAt: daysAgo(8),
        },
        {
            id: 12,
            novelId: 2,
            language: "English",
            sourceTerm: "劍宗",
            targetTerm: "the Sword Sect",
            category: "Organization",
            aliases: [],
            notes: null,
            origin: "AiExtracted",
            confidence: 0.9,
            needsReview: false,
            firstSeenChapterId: 20_001,
            updatedAt: daysAgo(8),
        },
        {
            id: 13,
            novelId: 2,
            language: "English",
            sourceTerm: "長夜",
            targetTerm: "the Long Night",
            category: "Other",
            aliases: [],
            notes: "Both the era and the book's title. Capitalised in both uses.",
            origin: "AiExtracted",
            confidence: 0.71,
            needsReview: true,
            firstSeenChapterId: 20_001,
            updatedAt: daysAgo(7),
        },
    ];

    const jobs: TranslationJob[] = [
        {
            id: 1,
            novelId: 1,
            mode: "Translate",
            scopeKind: "Range",
            fromIndex: 121,
            toIndex: 187,
            chapterIds: [],
            state: "Completed",
            processedCount: 66,
            totalCount: 67,
            costUsd: 2.34,
            budgetUsd: 5,
            createdAt: daysAgo(6),
            startedAt: daysAgo(6),
            finishedAt: daysAgo(6),
            error: null,
            currentStep: null,
            stepIndex: null,
            stepCount: null,
        },
        {
            id: 2,
            novelId: 2,
            mode: "Translate",
            scopeKind: "Range",
            fromIndex: 1,
            toIndex: 40,
            chapterIds: [],
            state: "Paused",
            processedCount: 24,
            totalCount: 40,
            costUsd: 0.75,
            budgetUsd: 0.75,
            createdAt: daysAgo(7),
            startedAt: daysAgo(7),
            finishedAt: null,
            error: null,
            currentStep: null,
            stepIndex: null,
            stepCount: null,
        },
    ];

    // One exchange already on record, so the screen does not open empty and the tool turn is visible
    // from the start — seeing what the agent did is the reason those turns are kept at all.
    const chat: Record<number, ChatMessage[]> = {
        1: [
            {
                id: 1,
                role: "User",
                text: "The alchemists' quarter should read as a district, not an organisation.",
                costUsd: 0,
                createdAt: daysAgo(2),
            },
            {
                id: 2,
                role: "Tool",
                text: "glossary_record — 錬金術師の区画 → the alchemists' quarter (Place)",
                costUsd: 0,
                createdAt: daysAgo(2),
            },
            {
                id: 3,
                role: "Agent",
                text: "Recorded it as a place. Chapters translated from here on will follow that; the "
                    + "six already done still say organisation, so re-run them if you want it consistent.",
                costUsd: 0.014,
                createdAt: daysAgo(2),
            },
        ],
    };

    return {
        novels,
        chapters: [...ashTown, ...longNight],
        glossary,
        jobs,
        // Import history is not worth seeding, unlike the translation jobs above: the activity
        // endpoint only ever reports live ones, and the mock backend creates these itself the moment
        // a screen starts one.
        imports: [],
        // The server's own defaults, deliberately, so working against mocks shows the same starting
        // point a real installation has rather than a set of numbers invented here.
        settings: {
            id: 1,
            globalStyleGuide: "Keep honorifics as transliterations. Prefer plain contemporary prose.",
            defaultModel: "sonnet",
            glossaryModel: "sonnet",
            localModelEndpoint: null,
            localModelName: "qwen2.5:7b",
            localModelApiKey: "not-needed",
            maxOutputTokens: 16000,
            expansionFactor: 2,
            voiceWindowChapters: 2,
            voiceWindowParagraphs: 4,
            pageLoadTimeoutMs: 45000,
            chatMaxRounds: 5,
            updatedAt: daysAgo(12),
        },
        parsers: PARSERS.map(parser => ({ ...parser })),
        parserSources: { "ncode.syosetu.com": SAMPLE_PARSER_SOURCE },
        chat,
    };
}
