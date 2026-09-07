using System.Security.Cryptography;
using System.Text;


namespace NektoTranslate.Translation.Services;


// Remembers translated batches so an interrupted chapter resumes instead of restarting.
//
// Passed in per call rather than injected into the translator: the translator is stateless and
// knows nothing about chapters, while only the caller knows which chapter these batches belong to.
// A caller with nothing to persist to passes NullBatchCache and the behaviour is unchanged.
public interface IBatchCache {

    Task<string?> TryGetAsync(string sourceHash, CancellationToken cancellationToken = default);


    Task StoreAsync(
        string sourceHash,
        int index,
        string translatedText,
        double costUsd,
        CancellationToken cancellationToken = default
    );
}


public sealed class NullBatchCache : IBatchCache {

    public static readonly NullBatchCache instance = new NullBatchCache();


    public Task<string?> TryGetAsync(string sourceHash, CancellationToken cancellationToken = default) {
        return Task.FromResult<string?>(null);
    }


    public Task StoreAsync(
        string sourceHash,
        int index,
        string translatedText,
        double costUsd,
        CancellationToken cancellationToken = default
    ) {
        return Task.CompletedTask;
    }
}


public static class BatchHash {

    // Written as a cast rather than an escape sequence so no build step or editor can mangle it.
    // A control character cannot occur in prose, so two different inputs cannot hash alike by
    // running into one another.
    private static readonly char separator = (char)1;


    // Hashes the batch's source text together with everything that would legitimately change its
    // translation. A different model or target language must not reuse a cached batch, and neither
    // must a chapter whose glossary has since been corrected - reusing it there would preserve
    // exactly the rendering the user just fixed.
    public static string Of(
        IReadOnlyList<string> segments,
        string language,
        string model,
        string glossaryFingerprint
    ) {
        StringBuilder material = new StringBuilder();

        material
            .Append(language)
            .Append(separator)
            .Append(model)
            .Append(separator)
            .Append(glossaryFingerprint);

        foreach (string segment in segments) {
            material.Append(separator).Append(segment);
        }

        return Hash(material.ToString());
    }


    // A careful pass's own hash: the body plus its surrounding context, since the context shapes
    // the answer just as much as the body does - the same chapter's paragraph translated with a
    // different neighbour on either side is not the same request. Context lines are marked apart
    // from body lines so a context paragraph and a body paragraph that happen to read identically
    // cannot be mistaken for one another in the hashed material.
    public static string Of(
        IReadOnlyList<string> contextBefore,
        IReadOnlyList<string> body,
        IReadOnlyList<string> contextAfter,
        string language,
        string model,
        string glossaryFingerprint
    ) {
        StringBuilder material = new StringBuilder();

        material
            .Append(language)
            .Append(separator)
            .Append(model)
            .Append(separator)
            .Append(glossaryFingerprint);

        foreach (string segment in contextBefore) {
            material.Append(separator).Append("before:").Append(segment);
        }

        foreach (string segment in body) {
            material.Append(separator).Append(segment);
        }

        foreach (string segment in contextAfter) {
            material.Append(separator).Append("after:").Append(segment);
        }

        return Hash(material.ToString());
    }


    // The glossary reaches the model through the system prompt, so a change to it changes the
    // translation. Folding it into the key is what makes a corrected term take effect on re-run
    // instead of being served from cache.
    public static string FingerprintOf(IEnumerable<(string source, string target)> terms) {
        StringBuilder material = new StringBuilder();

        foreach ((string source, string target) in terms.OrderBy(term => term.source, StringComparer.Ordinal)) {
            material.Append(source).Append(separator).Append(target).Append(separator);
        }

        return Hash(material.ToString());
    }


    private static string Hash(string material) {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
