import type { StatusMessage } from "@/types/models/domain";


// Translations of server status codes, keyed by the code the server sent.
//
// The server never translates. It names what happened with a stable code and describes it once in
// English; this table is where other languages live. A code with no entry here falls back to the
// server's English, so a missing translation is a sentence in the wrong language rather than a
// blank - and adding a language is adding entries here, with nothing to change on the server.
//
// Placeholders are `{name}` and are filled from the status's `args`, so a translation places the
// chapter number where its own grammar wants it rather than where the English put it.
const translations: Record<string, Record<string, string>> = {
    ru: {
        NO_CHAPTER_AT_INDEX: "Главы {index} нет. Сначала импортируйте оригинал или сдвиньте раскладку.",
        TRANSLATION_ALREADY_EXISTS: "У главы {index} уже есть перевод на {language}. Удалите его, если нужно заменить.",
        IMPORTED_TEXT_EMPTY: "Текст главы {index} оказался пустым.",
        CHAPTER_FETCH_FAILED: "Не удалось прочитать {url}: {reason}",
        MOVE_TARGET_MISSING: "Главы на позиции {target} нет.",
        MOVE_TARGET_OCCUPIED: "У главы {target} уже есть перевод, не входящий в этот перенос.",
        CHAPTER_BUSY: "Глава в очереди или переводится. Прогон затёр бы правку.",
        TRANSLATION_STALE: "Главу перевели заново после начала правки. Перезагрузите и внесите изменение в текущий текст.",
        BLOCK_OUT_OF_RANGE: "Такого блока в этом переводе нет.",
        NO_TRANSLATION_IN_LANGUAGE: "У этой главы ещё нет перевода на {language}.",
        BLOCK_UNCHANGED: "Этот блок вернулся без изменений — как в оригинале.",
        SOURCE_SCRIPT_RESIDUE: "В переводе остался фрагмент исходного письма: «{run}»",
        GLOSSARY_TERM_IGNORED: "Устоявшееся написание «{source}» → «{target}» не использовано.",
        LOCAL_MODEL_NOT_CONFIGURED: "Адрес локальной модели не задан.",
        LOCAL_MODEL_TIMED_OUT: "Сервер локальной модели не ответил вовремя.",
        LOCAL_MODEL_BAD_RESPONSE: "{url} ответил {status}.",
        LOCAL_MODEL_LIST_FAILED: "Не удалось получить список локальных моделей: {reason}",
        MODEL_REJECTED: "Модель отклонена: {reason}",
        MODEL_REJECTED_WITHOUT_REASON: "Модель отклонена, и CLI не объяснил почему.",
    },
};


// The language the interface is showing. A single switch for now; wiring it to a setting is a
// one-line change here and nowhere else.
let current = "en";


export function setStatusLanguage(language: string): void {
    current = language;
}


// The sentence to show for a status: the translation when one exists, the server's English when it
// does not. Never empty and never a bare code.
export function describe(status: StatusMessage): string {
    const template = translations[current]?.[status.code];

    if (template === undefined) {
        return status.text;
    }

    return template.replace(/\{(\w+)\}/g, (whole, name: string) => {
        const value = status.args?.[name];

        return value === undefined || value === null ? whole : String(value);
    });
}
