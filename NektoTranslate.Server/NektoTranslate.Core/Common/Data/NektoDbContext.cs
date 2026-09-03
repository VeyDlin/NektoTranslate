using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NektoTranslate.Chapters.Entities;
using NektoTranslate.Chat.Entities;
using NektoTranslate.Glossary.Entities;
using NektoTranslate.Jobs.Entities;
using NektoTranslate.Novels.Entities;
using NektoTranslate.Parsing.Entities;
using NektoTranslate.Settings.Entities;
using NektoTranslate.Translation.Entities;


namespace NektoTranslate.Common.Data;


public class NektoDbContext(DbContextOptions<NektoDbContext> options) : DbContext(options) {

    public DbSet<Novel> novels => Set<Novel>();

    public DbSet<Chapter> chapters => Set<Chapter>();

    public DbSet<ChapterTranslation> chapterTranslations => Set<ChapterTranslation>();

    public DbSet<GlossaryEntry> glossaryEntries => Set<GlossaryEntry>();

    public DbSet<TranslationJob> translationJobs => Set<TranslationJob>();

    public DbSet<ParserScript> parserScripts => Set<ParserScript>();

    public DbSet<ChatMessage> chatMessages => Set<ChatMessage>();

    public DbSet<ChapterChunk> chapterChunks => Set<ChapterChunk>();

    public DbSet<ApplicationSettings> applicationSettings => Set<ApplicationSettings>();

    public DbSet<ChapterTranslationIssue> chapterTranslationIssues => Set<ChapterTranslationIssue>();


    // SQLite has no date type and refuses to ORDER BY a DateTimeOffset, which every "most recent
    // first" query in the application needs. Storing UTC ticks keeps the expressive type in the
    // domain and gives the database a plain integer it can sort. Correct because every timestamp
    // here is written as UtcNow - a converter that kept the offset would sort wrongly across zones.
    private static readonly ValueConverter<DateTimeOffset, long> timestampConverter = new(
        value => value.UtcTicks,
        value => new DateTimeOffset(value, TimeSpan.Zero)
    );

    private static readonly ValueConverter<DateTimeOffset?, long?> nullableTimestampConverter = new(
        value => value!.Value.UtcTicks,
        value => new DateTimeOffset(value!.Value, TimeSpan.Zero)
    );


    protected override void OnModelCreating(ModelBuilder builder) {
        base.OnModelCreating(builder);

        foreach (IMutableEntityType entity in builder.Model.GetEntityTypes()) {
            foreach (IMutableProperty property in entity.GetProperties()) {
                if (property.ClrType == typeof(DateTimeOffset)) {
                    property.SetValueConverter(timestampConverter);
                } else if (property.ClrType == typeof(DateTimeOffset?)) {
                    property.SetValueConverter(nullableTimestampConverter);
                }
            }
        }

        builder.Entity<Novel>(novel => {
            novel.HasMany(n => n.chapters)
                .WithOne(c => c.novel!)
                .HasForeignKey(c => c.novelId)
                .OnDelete(DeleteBehavior.Cascade);

            novel.HasMany(n => n.glossary)
                .WithOne(g => g.novel!)
                .HasForeignKey(g => g.novelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Chapter>(chapter => {
            chapter.HasIndex(c => new { c.novelId, c.index }).IsUnique();
            chapter.HasIndex(c => new { c.novelId, c.translationState });

            chapter.HasMany(c => c.translations)
                .WithOne(t => t.chapter!)
                .HasForeignKey(t => t.chapterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChapterTranslation>(translation => {
            translation.HasIndex(t => new { t.chapterId, t.language });
        });

        // One rendering per source term per target language. A second rendering of the same term
        // is the exact failure this whole design exists to prevent, so the database refuses it
        // rather than leaving two rows for the model to choose between.
        builder.Entity<GlossaryEntry>(entry => {
            entry.HasIndex(g => new { g.novelId, g.language, g.sourceTerm }).IsUnique();
        });

        // One override per host: a second row for the same site would leave the runner choosing
        // between two definitions with nothing to choose on.
        builder.Entity<ParserScript>(script => {
            script.HasIndex(s => s.hostName).IsUnique();
        });

        // Lookup is by chapter, language and content hash - the three that together decide
        // whether a cached batch may be reused.
        builder.Entity<ChapterChunk>(chunk => {
            chunk.HasIndex(c => new { c.chapterId, c.language, c.sourceHash }).IsUnique();

            chunk.HasOne(c => c.chapter!)
                .WithMany()
                .HasForeignKey(c => c.chapterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChatMessage>(message => {
            message.HasIndex(m => new { m.novelId, m.id });

            message.HasOne(m => m.novel!)
                .WithMany()
                .HasForeignKey(m => m.novelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // The two questions actually asked of this table: "what is wrong with this chapter" when one
        // is opened, and "which chapters still need a look" after a run of two hundred. The second
        // is why state is in the index - without it that query reads every finding ever recorded.
        builder.Entity<ChapterTranslationIssue>(issue => {
            issue.HasIndex(i => new { i.chapterId, i.language });
            issue.HasIndex(i => i.state);

            issue.HasOne(i => i.chapter!)
                .WithMany()
                .HasForeignKey(i => i.chapterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TranslationJob>(job => {
            job.HasIndex(j => new { j.novelId, j.state });

            job.HasOne(j => j.novel!)
                .WithMany()
                .HasForeignKey(j => j.novelId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
