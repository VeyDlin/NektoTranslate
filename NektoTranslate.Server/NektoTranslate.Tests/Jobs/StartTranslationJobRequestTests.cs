using System.Text.Json;
using System.Text.Json.Serialization;
using NektoTranslate.Jobs.Contracts;
using NektoTranslate.Jobs.Enums;
using Xunit;


namespace NektoTranslate.Tests.Jobs;


// The body the run dialog actually sends, byte for byte in shape: every optional field present and
// null rather than omitted, enums as names. A voice-learning run was refused with "The JSON value
// could not be converted" because `force` was a non-nullable bool and the dialog, rightly, had
// nothing to say about forcing a run that does not translate. This pins the wire shape so a field
// that only some modes use can never again turn the other modes' requests into a 400.
public class StartTranslationJobRequestTests {

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter() }
    };


    [Fact]
    public void AVoiceLearningRequestWithNullForceDeserializes() {
        const string body = """
            {"mode":"LearnVoice","scopeKind":"Range","fromIndex":0,"toIndex":19,"chapterIds":null,"budgetUsd":null,"force":null}
            """;

        StartTranslationJobRequest? request = JsonSerializer.Deserialize<StartTranslationJobRequest>(body, WireOptions);

        Assert.NotNull(request);
        Assert.Equal(TranslationJobMode.LearnVoice, request.mode);
        Assert.Equal(JobScopeKind.Range, request.scopeKind);
        Assert.Equal(0, request.fromIndex);
        Assert.Equal(19, request.toIndex);
        Assert.Null(request.force);
    }


    [Fact]
    public void ATranslateRequestStillCarriesItsForceFlag() {
        const string body = """
            {"mode":"Translate","scopeKind":"WholeBook","fromIndex":null,"toIndex":null,"chapterIds":null,"budgetUsd":2.5,"force":true}
            """;

        StartTranslationJobRequest? request = JsonSerializer.Deserialize<StartTranslationJobRequest>(body, WireOptions);

        Assert.NotNull(request);
        Assert.True(request.force);
        Assert.Equal(2.5, request.budgetUsd);
    }
}
