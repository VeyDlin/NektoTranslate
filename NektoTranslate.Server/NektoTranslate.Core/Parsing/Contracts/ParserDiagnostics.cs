namespace NektoTranslate.Parsing.Contracts;


// What happened while the scripts were being loaded into the page. Surfaced rather than swallowed:
// a parser that silently failed to evaluate looks exactly like a site with no chapters, and the
// two need very different fixes.
public sealed record ParserDiagnostics(
    int coreLoaded,
    int parsersLoaded,
    IReadOnlyList<string> failures
);
