using NektoTranslate.Common.Contracts;


namespace NektoTranslate.Settings.Contracts;


// One entry in the model dropdown.
//
// The id is what reaches the CLI's --model, which accepts either a family alias or a full model
// name. Aliases are what the default catalogue uses: an alias always resolves to the newest model
// of its family, so a list built from aliases does not go stale when a new model ships. A full name
// is for someone who deliberately wants to pin a version and accept that it will age.
public sealed record ModelOption(
    string id,
    string label,
    string description,
    bool isAlias
);


// A model's availability as actually observed, rather than as listed.
//
// The catalogue is what this installation offers; whether a given subscription can run a given
// model is a different question, and only the CLI can answer it.
public sealed record ModelProbeResult(
    string id,
    bool available,
    Status? error
);


// What a locally hosted server reports it has loaded.
//
// Unlike the Claude side this is a real enumeration: every OpenAI-compatible server answers
// GET /v1/models with the list it can actually serve.
public sealed record LocalModelList(
    bool reachable,
    IReadOnlyList<string> models,
    Status? error
);
