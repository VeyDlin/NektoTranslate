using NektoTranslate.Glossary.Contracts;


namespace NektoTranslate.Glossary.Services;


// Finds where a term occurs on the source side of a novel.
//
// Deliberately an interface with a swappable implementation. Exact substring matching is an
// assumption, not a law: it holds for Japanese, Chinese and Korean, where names do not inflect and
// agglutinated particles do not break a substring search, and it fails once the source language
// itself declines names. Stem, fuzzy and vector implementations slot in here without touching
// anything else.
public interface ITermLocator {

    Task<IReadOnlyList<TermOccurrence>> FindAsync(
        long novelId,
        string term,
        int limit,
        CancellationToken cancellationToken = default
    );
}
