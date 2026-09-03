namespace NektoTranslate.Glossary.Contracts;


public sealed record ResolvedTerm(
    string sourceTerm,
    string targetTerm,
    long evidenceChapterId,
    double costUsd
);
