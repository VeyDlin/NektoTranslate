using System.Text.Json;
using NektoTranslate.Common.Contracts;


namespace NektoTranslate.Translation.Services;


// How a finding's Status is taken apart for the database and put back together on the way out.
//
// Three columns rather than one JSON blob, because they are read differently: the code is what a
// translation is looked up by, the English is what an unknown code falls back to, and the arguments
// are only ever handed straight through. Both directions live here so they cannot drift apart.
public static class IssueStatus {

    public static string? ArgsOf(Status status) {
        return status.args is null || status.args.Count == 0
            ? null
            : JsonSerializer.Serialize(status.args);
    }


    public static Status Rebuild(string code, string message, string? argsJson) {
        IReadOnlyDictionary<string, object?>? args = argsJson is null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson);

        return new Status(code, message, args);
    }
}
