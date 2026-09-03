using NektoTranslate.Settings.Contracts;
using NektoTranslate.Settings.Entities;
using NektoTranslate.Settings.Services;


namespace NektoTranslate.Tests.Common;


// Settings without a database, for tests of code that only reads one or two fields.
//
// Holds a real ApplicationSettings rather than returning fabricated numbers, so a test runs against
// the same defaults the application ships with - a stub that invented its own values would let a bad
// default through unnoticed.
public class StubSettingsService(ApplicationSettings? settings = null) : ISettingsService {

    public ApplicationSettings current { get; } = settings ?? new ApplicationSettings();


    public Task<ApplicationSettings> GetAsync(CancellationToken cancellationToken = default) {
        return Task.FromResult(current);
    }


    public Task<ApplicationSettings> UpdateAsync(
        UpdateSettingsRequest request,
        CancellationToken cancellationToken = default
    ) {
        return Task.FromResult(current);
    }
}
