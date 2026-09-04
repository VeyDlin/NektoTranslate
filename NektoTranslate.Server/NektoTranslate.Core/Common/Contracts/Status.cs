using System.Text.RegularExpressions;


namespace NektoTranslate.Common.Contracts;


// Everything the server says to a person, in one shape.
//
// The server does not translate. It names what happened with a stable code and describes it once,
// in English, and the interface decides what to show: its own translation of the code when it has
// one, the English text when it does not. Either way the sentence that reaches the reader is never
// assembled here, which is what lets a language be added without touching the server at all.
//
// The code is the contract and is written the way Telegram writes its statuses - upper case, words
// joined by underscores - because that is what a translation file is keyed on, and a key that is
// also readable is one that stays correct when the English changes underneath it.
//
// Arguments travel separately from the text. A translation needs the chapter number as a value it
// can place where its own grammar wants it, not baked into an English sentence it has to parse.
public sealed partial record Status(
    string code,
    string text,
    IReadOnlyDictionary<string, object?>? args = null
) {

    // Fills {name} placeholders in the English text and keeps the values alongside, so the fallback
    // reads naturally and a translation still gets the raw values.
    public Status With(params (string name, object? value)[] values) {
        Dictionary<string, object?> merged = args is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(args);

        foreach ((string name, object? value) in values) {
            merged[name] = value;
        }

        string filled = Placeholder().Replace(text, match => {
            string name = match.Groups[1].Value;

            return merged.TryGetValue(name, out object? value) ? value?.ToString() ?? string.Empty : match.Value;
        });

        return new Status(code, filled, merged);
    }


    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
