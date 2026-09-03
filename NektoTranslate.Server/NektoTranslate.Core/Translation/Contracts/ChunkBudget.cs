using NektoTranslate.Common.Models;
using NektoTranslate.Settings.Entities;


namespace NektoTranslate.Translation.Contracts;


// How much source may go into one request.
//
// Derived from the model's *output* ceiling rather than chosen for the input, because the output
// ceiling is what actually truncates a reply. Budgeting the input directly says nothing about
// whether the answer will fit, and the failure it produces costs the whole batch.
//
// Carried on the request rather than fixed in the chunker so it can come from settings - the
// expansion factor in particular is worth tuning per installation, since Latin to Cyrillic roughly
// doubles the token count while Japanese to Russian barely grows.
public sealed record ChunkBudget(
    int maxCharacters,
    int maxTokens
) {

    public static ChunkBudget From(ApplicationSettings settings, BatchingOptions batching) {
        int tokens = (int)((settings.maxOutputTokens - batching.safetyMargin) / settings.expansionFactor);

        return new ChunkBudget(
            batching.maxCharacters,
            Math.Max(batching.minimumTokens, tokens)
        );
    }


    // Used where no settings are available - tests and any caller that has not been given a budget.
    public static ChunkBudget Default { get; } = From(new ApplicationSettings(), new BatchingOptions());
}
