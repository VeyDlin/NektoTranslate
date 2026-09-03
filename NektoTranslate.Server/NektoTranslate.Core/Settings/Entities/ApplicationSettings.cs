using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace NektoTranslate.Settings.Entities;


// Application-wide settings. Exactly one row, pinned to id 1.
//
// A single row rather than a key/value table because these are a fixed, small set with real types.
// A key/value store would turn every read into a parse and every typo into a silent default.
//
// What belongs here is anything with a real trade-off for the user - quality against money, speed
// against reliability. What does not belong here is anything with one right answer; a field for
// that only invites breaking what works. The segment protocol in particular stays in code.
//
// Every numeric field is clamped on write (see SettingsService). Exposing a dial is only safe if
// the dial cannot be turned to a position that breaks the pipeline.
[Table("application_settings")]
public class ApplicationSettings {

    public const long SingletonId = 1;


    [Key]
    public long id { get; set; } = SingletonId;

    // --- prompts -------------------------------------------------------------------------------

    // Applies to every novel, additive to the built-in prompt, and overridden by a novel's own.
    public string? globalStyleGuide { get; set; }

    // --- models --------------------------------------------------------------------------------

    // Default translation model for newly created novels. Each novel keeps its own afterwards.
    [MaxLength(64)]
    public string defaultModel { get; set; } = "sonnet";

    // Model for the glossary's mechanical calls - listing proper nouns, reading how a term was
    // rendered. Deliberately separate from the translation model: those calls need reading
    // comprehension rather than literary judgement, and there are far more of them.
    [MaxLength(64)]
    public string glossaryModel { get; set; } = "sonnet";

    // --- local model ---------------------------------------------------------------------------

    // Any OpenAI-compatible endpoint - Ollama, LM Studio, llama.cpp, vLLM. Empty means the glossary
    // calls stay on the subscription. Ollama's compatibility surface is http://localhost:11434/v1
    [MaxLength(500)]
    public string? localModelEndpoint { get; set; }

    [MaxLength(128)]
    public string localModelName { get; set; } = "qwen2.5:7b";

    [MaxLength(256)]
    public string localModelApiKey { get; set; } = "not-needed";

    // --- batching ------------------------------------------------------------------------------

    // The model's output ceiling. What actually truncates a reply, and therefore what the input
    // budget is derived from.
    public int maxOutputTokens { get; set; } = 16_000;

    // How much larger the translation is expected to be than its source, in tokens.
    //
    // The default is conservative because it must hold for every pair. It is worth tuning per
    // installation: Latin to Cyrillic really does roughly double, but Japanese to Russian barely
    // grows, and leaving it at 2.0 there wastes half of every batch.
    public double expansionFactor { get; set; } = 2.0;

    // --- context -------------------------------------------------------------------------------

    // How many already-translated chapters are sampled as an example of the established voice, and
    // how much of each. A direct trade of cost against consistency, which is why it is a setting
    // rather than a constant.
    public int voiceWindowChapters { get; set; } = 2;

    public int voiceWindowParagraphs { get; set; } = 4;

    // --- limits --------------------------------------------------------------------------------

    // How long to wait for a site to load before giving up.
    public int pageLoadTimeoutMs { get; set; } = 45_000;

    // How many tool calls the agent may make before it must answer. A ceiling on what one chat
    // message can cost.
    public int chatMaxRounds { get; set; } = 5;

    public DateTimeOffset updatedAt { get; set; } = DateTimeOffset.UtcNow;
}
