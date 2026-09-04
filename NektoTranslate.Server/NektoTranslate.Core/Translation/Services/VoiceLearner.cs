using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeCodeSdk;
using ClaudeCodeSdk.Types;
using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Common.Models;
using NektoTranslate.Glossary.Enums;
using NektoTranslate.Glossary.Services;
using NektoTranslate.Settings.Entities;
using NektoTranslate.Settings.Services;
using NektoTranslate.Translation.Contracts;
using NektoTranslate.Translation.Entities;


namespace NektoTranslate.Translation.Services;


// Learns a VoiceProfile and a set of TranslationTerm rows from a span of chapters that already
// carry a human translation, with no original anywhere to anchor against.
//
// This is deliberately not the glossary, and the difference is not cosmetic. GlossaryEntry is keyed
// by the *source* term - ExactTermLocator finds it in Chapter.sourcePlainText and then reads the
// aligned translated paragraph, which assumes a source exists to search. A book imported as somebody
// else's translation has none: sourceMarkdown and sourcePlainText are null on every chapter of it,
// and this class never reads either field. TranslationTerm is keyed by the rendering itself instead,
// because the rendering is the only side of the pair there is left to read. A reader expecting this
// to duplicate the glossary will be wrong in the one detail that matters: there is no sourceTerm
// column here, because there is nothing to put in it.
//
// Runs on ApplicationSettings.glossaryModel rather than the novel's own translation model, for the
// same reason the glossary's own mechanical calls do: listing the names a passage uses and describing
// how it reads is reading comprehension, not the literary judgement a translation pays for, and it
// must not be billed at the book's rate.
public class VoiceLearner(
    NektoDbContext database,
    ISettingsService settings,
    EngineOptions engine
) : IVoiceLearner {

    private readonly BatchingOptions batching = engine.batching;

    private sealed record SampleChapter(long id, string plainText);


    public async Task<LearnedVoice> LearnAsync(
        long novelId,
        string language,
        int fromChapterIndex,
        int toChapterIndex,
        CancellationToken cancellationToken = default
    ) {
        List<SampleChapter> chapters = await LoadSampleAsync(
            novelId,
            language,
            fromChapterIndex,
            toChapterIndex,
            cancellationToken
        );

        if (chapters.Count == 0) {
            throw new InvalidOperationException(
                $"Novel {novelId} has no {language} translation between chapters {fromChapterIndex} "
                + $"and {toChapterIndex} to learn a voice from."
            );
        }

        // One segment per paragraph rather than one per chapter, so a chapter with a single dense
        // scene does not become an all-or-nothing unit that either wholly fits a batch or wholly
        // triggers SegmentChunker's oversized-piece splitting. Kept alongside the chapter each
        // paragraph came from, so a term can still be traced back to the chapter it was first read
        // in even after paragraphs from several chapters have been merged into one request.
        List<string> segments = [];
        List<long> segmentChapterIds = [];

        foreach (SampleChapter chapter in chapters) {
            foreach (string paragraph in VoicePrompt.SplitParagraphs(chapter.plainText)) {
                segments.Add(paragraph);
                segmentChapterIds.Add(chapter.id);
            }
        }

        ApplicationSettings applicationSettings = await settings.GetAsync(cancellationToken);
        string model = applicationSettings.glossaryModel;
        ChunkBudget budget = ChunkBudget.From(applicationSettings, batching);

        // A sample spanning nineteen chapters - or a hundred - is routinely larger than one request
        // can hold, and sending it whole is exactly the mistake this guards against: a reply cut off
        // mid-way would silently lose the second half of the book's own vocabulary and read as
        // though nothing past that point existed. Reusing SegmentChunker instead of a bespoke cut
        // keeps the cutting rules - paragraph, then sentence, then a blind character window -
        // identical to the ones ChapterTranslator and ChapterRepairer already rely on.
        (List<string> pieces, List<int> owners) = SegmentChunker.Flatten(segments, budget);
        List<IReadOnlyList<string>> requestBatches = SegmentChunker.Partition(pieces, budget);

        Dictionary<string, MergedTerm> merged = new(StringComparer.Ordinal);
        List<string> voiceNotes = [];
        double costUsd = 0;
        int pieceCursor = 0;

        foreach (IReadOnlyList<string> batch in requestBatches) {
            // Partition consumes pieces in order without reordering or dropping any of them, so the
            // chapter that owns this batch's first piece is the chapter this batch mostly belongs
            // to - an approximation of "first seen" that is exact whenever a batch does not straddle
            // a chapter boundary, and only ever a chapter or two off when it does.
            long batchChapterId = segmentChapterIds[owners[pieceCursor]];
            pieceCursor += batch.Count;

            (ChunkExtraction extraction, double batchCost) = await ExtractChunkAsync(
                language,
                model,
                batch,
                cancellationToken
            );

            costUsd += batchCost;

            if (extraction.voiceNotes.Length > 0) {
                voiceNotes.Add(extraction.voiceNotes);
            }

            VoicePrompt.MergeTerms(merged, extraction.terms, batchChapterId);
        }

        (string summary, double synthesisCost) = await SummarizeVoiceAsync(language, model, voiceNotes, cancellationToken);
        costUsd += synthesisCost;

        VoiceProfile profile = new VoiceProfile {
            novelId = novelId,
            language = language,
            summary = summary,
            fromChapterIndex = fromChapterIndex,
            toChapterIndex = toChapterIndex,
            model = model,
            costUsd = costUsd
        };

        database.voiceProfiles.Add(profile);

        string corpus = string.Join("\n", segments);

        foreach (MergedTerm term in merged.Values) {
            int occurrences = VoicePrompt.CountOccurrences(corpus, term.term, term.variants);

            await UpsertTermAsync(novelId, language, term, occurrences, cancellationToken);
        }

        await database.SaveChangesAsync(cancellationToken);

        return new LearnedVoice(profile.id, profile.summary, merged.Count, costUsd);
    }


    private async Task<List<SampleChapter>> LoadSampleAsync(
        long novelId,
        string language,
        int fromChapterIndex,
        int toChapterIndex,
        CancellationToken cancellationToken
    ) {
        var rows = await database.chapters
            .AsNoTracking()
            .Where(chapter => chapter.novelId == novelId
                && chapter.index >= fromChapterIndex
                && chapter.index <= toChapterIndex)
            .OrderBy(chapter => chapter.index)
            .Select(chapter => new {
                chapter.id,
                // The newest rendering, whatever produced it - imported, manual, machine, already
                // repaired. Voice learning reads what is on the page today, not how it got there.
                translation = chapter.translations
                    .Where(translation => translation.language == language)
                    .OrderByDescending(translation => translation.createdAt)
                    .ThenByDescending(translation => translation.id)
                    .Select(translation => translation.plainText)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // A chapter inside the range with no translation yet is skipped rather than failing the
        // whole run - a range picked from the alignment screen can include a gap, and the sample is
        // still worth learning from without it.
        return rows
            .Where(row => row.translation is not null)
            .Select(row => new SampleChapter(row.id, row.translation!))
            .ToList();
    }


    private static async Task<(ChunkExtraction extraction, double costUsd)> ExtractChunkAsync(
        string language,
        string model,
        IReadOnlyList<string> batch,
        CancellationToken cancellationToken
    ) {
        (string reply, double costUsd) = await AskAsync(
            VoicePrompt.BuildChunkSystemPrompt(language),
            string.Join("\n\n", batch),
            model,
            cancellationToken
        );

        return (VoicePrompt.Parse(reply), costUsd);
    }


    // One passage's worth of observations is the summary outright - there is nothing to reconcile.
    // More than one is where a second small call earns its cost: without it the profile would be
    // whichever passage happened to run first or last, or the several notes stitched together
    // unread, rather than one account of the whole sample.
    private static async Task<(string summary, double costUsd)> SummarizeVoiceAsync(
        string language,
        string model,
        IReadOnlyList<string> voiceNotes,
        CancellationToken cancellationToken
    ) {
        if (voiceNotes.Count == 0) {
            return ("No distinctive voice was observed in the sampled chapters.", 0);
        }

        if (voiceNotes.Count == 1) {
            return (voiceNotes[0], 0);
        }

        (string reply, double costUsd) = await AskAsync(
            VoicePrompt.BuildSynthesisSystemPrompt(language),
            string.Join("\n\n", voiceNotes.Select((note, index) => $"Passage {index + 1}:\n{note}")),
            model,
            cancellationToken
        );

        return (reply.Trim(), costUsd);
    }


    // Re-running over the same or an overlapping range should grow what is already known rather
    // than replace it: a second pass reads a different slice of the sample and may catch a spelling
    // or a mention the first pass missed, but it must never forget what the first pass already
    // settled. firstSeenChapterId and category are written once, at creation, and left alone after -
    // they describe how the term was first met, and a later pass rereading the same book does not
    // change that history.
    private async Task UpsertTermAsync(
        long novelId,
        string language,
        MergedTerm term,
        int occurrences,
        CancellationToken cancellationToken
    ) {
        TranslationTerm? existing = await database.translationTerms.FirstOrDefaultAsync(
            row => row.novelId == novelId && row.language == language && row.term == term.term,
            cancellationToken
        );

        if (existing is null) {
            database.translationTerms.Add(new TranslationTerm {
                novelId = novelId,
                language = language,
                term = term.term,
                variantsJson = JsonSerializer.Serialize(term.variants),
                category = term.category,
                notes = term.notes,
                occurrences = occurrences,
                firstSeenChapterId = term.firstSeenChapterId
            });

            return;
        }

        HashSet<string> variants = VoicePrompt.DecodeVariants(existing.variantsJson);
        variants.UnionWith(term.variants);

        existing.variantsJson = JsonSerializer.Serialize(variants);
        existing.occurrences += occurrences;
        existing.notes ??= term.notes;
    }


    private static async Task<(string reply, double costUsd)> AskAsync(
        string systemPrompt,
        string prompt,
        string model,
        CancellationToken cancellationToken
    ) {
        ClaudeCodeOptions options = new ClaudeCodeOptions {
            Model = model,
            SystemPrompt = systemPrompt,
            MaxTurns = 1,
            ExtraArgs = new Dictionary<string, string?> {
                { "safe-mode", null },
                { "tools", "" },
                { "no-session-persistence", null }
            }
        };

        StringBuilder reply = new StringBuilder();
        double costUsd = 0;

        await foreach (IMessage message in ClaudeQuery.QueryAsync(prompt, options, null, cancellationToken)) {
            if (message is AssistantMessage assistant) {
                foreach (TextBlock block in assistant.Content.OfType<TextBlock>()) {
                    reply.Append(block.Text);
                }
            }

            if (message is ResultMessage result) {
                costUsd += result.TotalCostUsd ?? 0;

                if (result.IsError) {
                    throw new InvalidOperationException($"Claude Code returned an error: {result.Result}");
                }
            }
        }

        return (reply.ToString(), costUsd);
    }
}


// A candidate term as one chunk-level reply reported it: the canonical spelling, its category, the
// inflected forms or alternate spellings that same passage happened to show, and a note if the model
// offered one. Not yet merged with what any other chunk found, and not yet counted - both happen
// once every batch has answered.
public sealed record ExtractedTerm(
    string term,
    GlossaryCategory category,
    IReadOnlyList<string> variants,
    string? notes
);


// What one chunk-level call answered: the candidate terms it read off that passage, and its
// observations about the translator's voice there. Two questions in one call rather than two calls,
// because both are read from the same text at the same time and asking twice would pay for the
// passage's worth of tokens a second time for no better an answer.
public sealed record ChunkExtraction(
    IReadOnlyList<ExtractedTerm> terms,
    string voiceNotes
);


// One term as it stands after folding together every chunk that mentioned it. Mutable where a later
// chunk may still add to what an earlier one found - the variant spellings, a missing note - and
// fixed where it may not: the category and the chapter a term is credited as first seen in are set
// once, from whichever chunk reached it first in reading order, because rereading the same passage
// under a different chunk boundary should not rewrite either.
public sealed class MergedTerm {
    public required string term { get; init; }

    public required GlossaryCategory category { get; init; }

    public HashSet<string> variants { get; init; } = new(StringComparer.Ordinal);

    public string? notes { get; set; }

    public required long firstSeenChapterId { get; init; }
}


// The pure half of voice learning: building the two prompts a learning pass sends, parsing what
// comes back, folding chunk-level findings into one term list, and counting how often a term
// actually occurs. None of it touches the database or the model, which is what makes it worth
// testing directly - this is where an off-by-one would silently under- or double-count a name across
// a whole book.
public static class VoicePrompt {

    private const string VoiceHeading = "VOICE:";

    private const string TermsHeading = "TERMS:";

    private const string NoTerms = "NONE";


    public static string BuildChunkSystemPrompt(string language) {
        StringBuilder prompt = new StringBuilder();

        prompt.AppendLine(
            $"You are reading one passage of an existing {language} translation of a light novel. "
            + "There is no original text to compare it against and none is coming - read only how "
            + "this translator actually wrote this passage."
        );
        prompt.AppendLine("Answer in exactly the two sections below, in this order, and nothing else.");

        prompt.AppendLine();
        prompt.AppendLine(VoiceHeading);
        prompt.AppendLine(
            "In English, a few sentences on this translator's voice in this passage: register "
            + "(formal or casual), how dialogue is punctuated, how honorifics and names are handled, "
            + "sentence length and rhythm, and any recurring vocabulary or turns of phrase."
        );

        prompt.AppendLine();
        prompt.AppendLine(TermsHeading);
        prompt.AppendLine(
            "One line per recurring proper noun or named term this passage uses - a person, place, "
            + "organisation, named technique or named item - exactly as the translation spells it, "
            + "in this form:"
        );
        prompt.AppendLine("term | category | variant, variant | notes");
        prompt.AppendLine(
            "Category is one of Person, Place, Organization, Technique, Item, Other. Variants are "
            + "other inflected forms or alternate spellings of the SAME term seen in this passage, "
            + "comma separated - leave the field empty if there are none. Notes are a short "
            + "clarification such as gender, role or title - leave the field empty if there is "
            + "nothing worth noting. Do not invent a term this passage does not actually use."
        );
        prompt.AppendLine($"If this passage names nothing worth tracking, write {NoTerms} under {TermsHeading}");

        return prompt.ToString();
    }


    public static string BuildSynthesisSystemPrompt(string language) {
        return
            $"You are given several separate sets of notes about how one human translator renders "
            + $"{language} prose, each written from a different passage of the same book. Merge them "
            + "into a single cohesive paragraph, in English, describing this translator's voice: "
            + "register, how dialogue is punctuated, how honorifics and names are handled, sentence "
            + "length and rhythm, and recurring vocabulary habits. Write it as an instruction for a "
            + "writer who must reproduce this voice, not as a report about it, and do not mention "
            + "that it was assembled from separate notes. Answer with the paragraph alone.";
    }


    public static ChunkExtraction Parse(string reply) {
        string normalized = reply.ReplaceLineEndings("\n");
        string voiceNotes = Section(normalized, VoiceHeading, TermsHeading).Trim();
        string termsSection = Section(normalized, TermsHeading, null).Trim();

        List<ExtractedTerm> terms = [];

        foreach (string line in termsSection.Split('\n')) {
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.Equals(NoTerms, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            ExtractedTerm? term = ParseTermLine(trimmed);

            if (term is not null) {
                terms.Add(term);
            }
        }

        return new ChunkExtraction(terms, voiceNotes);
    }


    // Folds one chunk's findings into the dictionary a whole learning run is accumulating. The
    // dictionary, not this function, is what survives across batches, so this only ever adds to it
    // or extends an entry already there - it never removes or replaces one.
    public static void MergeTerms(
        IDictionary<string, MergedTerm> merged,
        IReadOnlyList<ExtractedTerm> extracted,
        long chapterId
    ) {
        foreach (ExtractedTerm term in extracted) {
            if (merged.TryGetValue(term.term, out MergedTerm? existing)) {
                existing.variants.UnionWith(term.variants);
                existing.notes ??= term.notes;

                continue;
            }

            merged[term.term] = new MergedTerm {
                term = term.term,
                category = term.category,
                variants = new HashSet<string>(term.variants, StringComparer.Ordinal),
                notes = term.notes,
                firstSeenChapterId = chapterId
            };
        }
    }


    // How often the canonical form and its recorded variants occur in the sampled prose, counted
    // mechanically rather than trusted from the model's own say-so - a language model asked to count
    // is exactly as reliable as one asked to do arithmetic in its head.
    //
    // Every spelling is matched on its own exact wording rather than with TermMatching's tolerance
    // for a short inflectional tail. That tolerance is what lets a canonical "Иван" also match
    // "Ивана" and "Ивану" - useful for finding a term at all, but wrong for counting one: if "Ивана"
    // is also recorded as its own variant, counting the canonical form under that tolerant rule
    // would count every one of its occurrences a second time. Exact, mutually exclusive spellings
    // cost a rare unrecorded inflection going uncounted, which only softens a sort order - it never
    // inflates one.
    public static int CountOccurrences(string haystack, string term, IReadOnlyCollection<string> variants) {
        HashSet<string> spellings = new HashSet<string>(variants, StringComparer.Ordinal) { term };
        int total = 0;

        foreach (string spelling in spellings) {
            if (spelling.Length == 0) {
                continue;
            }

            total += TermMatching.IsIdeographic(spelling)
                ? CountSubstring(haystack, spelling)
                : CountWholeWord(haystack, spelling);
        }

        return total;
    }


    public static IEnumerable<string> SplitParagraphs(string plainText) {
        return plainText
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Where(line => line.Trim().Length > 0);
    }


    // A term whose variants failed to store as valid JSON is not a reason to fail a whole learning
    // run - it is treated as having no variants yet, exactly as RepairPrompt already treats the same
    // failure when reading a term back for a repair.
    public static HashSet<string> DecodeVariants(string variantsJson) {
        try {
            List<string>? decoded = JsonSerializer.Deserialize<List<string>>(variantsJson);

            return decoded is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(decoded, StringComparer.Ordinal);
        } catch (JsonException) {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }


    private static string Section(string text, string heading, string? nextHeading) {
        int start = text.IndexOf(heading, StringComparison.Ordinal);

        if (start < 0) {
            return string.Empty;
        }

        start += heading.Length;

        int end = nextHeading is null ? -1 : text.IndexOf(nextHeading, start, StringComparison.Ordinal);

        return end < 0 ? text[start..] : text[start..end];
    }


    private static ExtractedTerm? ParseTermLine(string line) {
        string[] fields = line.Split('|');
        string term = fields[0].Trim();

        if (term.Length == 0) {
            return null;
        }

        GlossaryCategory category = fields.Length > 1
            && Enum.TryParse(fields[1].Trim(), true, out GlossaryCategory parsedCategory)
            ? parsedCategory
            : GlossaryCategory.Other;

        List<string> variants = fields.Length > 2
            ? fields[2]
                .Split(',')
                .Select(variant => variant.Trim())
                .Where(variant => variant.Length > 0 && !variant.Equals(term, StringComparison.Ordinal))
                .ToList()
            : [];

        string? notes = fields.Length > 3 ? NullIfEmpty(fields[3].Trim()) : null;

        return new ExtractedTerm(term, category, variants, notes);
    }


    private static string? NullIfEmpty(string value) {
        return value.Length == 0 ? null : value;
    }


    private static int CountSubstring(string haystack, string term) {
        int count = 0;
        int index = 0;

        while ((index = haystack.IndexOf(term, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += term.Length;
        }

        return count;
    }


    private static int CountWholeWord(string haystack, string term) {
        return Regex.Matches(haystack, $@"\b{Regex.Escape(term)}\b", RegexOptions.IgnoreCase).Count;
    }
}
