using Microsoft.EntityFrameworkCore;
using NektoTranslate.Common.Data;
using NektoTranslate.Settings.Contracts;
using NektoTranslate.Settings.Entities;


namespace NektoTranslate.Settings.Services;


public interface ISettingsService {

    Task<ApplicationSettings> GetAsync(CancellationToken cancellationToken = default);


    Task<ApplicationSettings> UpdateAsync(
        UpdateSettingsRequest request,
        CancellationToken cancellationToken = default
    );
}


// Reads and writes the single settings row, creating it on first use.
//
// Created lazily rather than seeded by the migration: a seed would duplicate the entity's defaults
// in a second place, and the two would drift.
public class SettingsService(NektoDbContext database) : ISettingsService {

    public async Task<ApplicationSettings> GetAsync(CancellationToken cancellationToken = default) {
        ApplicationSettings? settings = await database.applicationSettings
            .FirstOrDefaultAsync(row => row.id == ApplicationSettings.SingletonId, cancellationToken);

        if (settings is not null) {
            return settings;
        }

        settings = new ApplicationSettings();

        database.applicationSettings.Add(settings);
        await database.SaveChangesAsync(cancellationToken);

        return settings;
    }


    public async Task<ApplicationSettings> UpdateAsync(
        UpdateSettingsRequest request,
        CancellationToken cancellationToken = default
    ) {
        ApplicationSettings settings = await GetAsync(cancellationToken);

        // Null means "leave alone" so a caller can change one field without sending the others.
        // An empty string is a deliberate clearing and is honoured.
        if (request.globalStyleGuide is not null) {
            settings.globalStyleGuide = Blank(request.globalStyleGuide);
        }

        if (request.localModelEndpoint is not null) {
            settings.localModelEndpoint = Blank(request.localModelEndpoint);
        }

        // Null means "leave alone" as usual; an empty string is the deliberate choice of "the
        // book's own model" rather than a pinned one.
        if (request.repairModel is not null) {
            settings.repairModel = Blank(request.repairModel);
        }

        settings.defaultModel = Text(request.defaultModel, settings.defaultModel);
        settings.glossaryModel = Text(request.glossaryModel, settings.glossaryModel);
        settings.localModelName = Text(request.localModelName, settings.localModelName);
        settings.localModelApiKey = Text(request.localModelApiKey, settings.localModelApiKey);

        // Ranges, not validation errors. A settings screen must not be able to leave the engine
        // unable to translate, and quietly correcting an absurd number beats refusing the save and
        // losing the user's other edits along with it.
        settings.maxOutputTokens = Clamp(request.maxOutputTokens, settings.maxOutputTokens, 2_000, 128_000);
        settings.expansionFactor = Clamp(request.expansionFactor, settings.expansionFactor, 0.5, 6.0);
        settings.voiceWindowChapters = Clamp(request.voiceWindowChapters, settings.voiceWindowChapters, 0, 10);
        settings.voiceWindowParagraphs = Clamp(request.voiceWindowParagraphs, settings.voiceWindowParagraphs, 0, 20);
        settings.passSegments = Clamp(request.passSegments, settings.passSegments, 1, 50);
        settings.passContextBefore = Clamp(request.passContextBefore, settings.passContextBefore, 0, 20);
        settings.passContextAfter = Clamp(request.passContextAfter, settings.passContextAfter, 0, 20);
        // 0 is a real, meaningful floor here - it is what turns thinking off - so the clamp must not
        // raise it the way every other minimum in this method does.
        settings.thinkingTokens = Clamp(request.thinkingTokens, settings.thinkingTokens, 0, 32_000);
        settings.proofread = request.proofread ?? settings.proofread;
        settings.pageLoadTimeoutMs = Clamp(request.pageLoadTimeoutMs, settings.pageLoadTimeoutMs, 5_000, 300_000);
        settings.chatMaxRounds = Clamp(request.chatMaxRounds, settings.chatMaxRounds, 1, 20);

        settings.updatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);

        return settings;
    }


    private static string? Blank(string value) {
        return value.Length == 0 ? null : value;
    }


    private static string Text(string? incoming, string current) {
        return string.IsNullOrWhiteSpace(incoming) ? current : incoming;
    }


    private static int Clamp(int? incoming, int current, int minimum, int maximum) {
        return incoming is null ? current : Math.Clamp(incoming.Value, minimum, maximum);
    }


    private static double Clamp(double? incoming, double current, double minimum, double maximum) {
        return incoming is null ? current : Math.Clamp(incoming.Value, minimum, maximum);
    }
}
