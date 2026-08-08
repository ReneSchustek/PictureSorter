using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Configurations;

/// <summary>
/// Legt Tabelle, Pflichtfelder und Indizes des Analyselaufs fest.
/// </summary>
internal sealed class AnalysisRunEntityConfiguration : IEntityTypeConfiguration<AnalysisRunEntity>
{
    private const int PathMaxLength = 1024;
    private const int CategoryMaxLength = 256;
    private const int ReasonMaxLength = 512;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AnalysisRunEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable("AnalysisRun");
        _ = builder.HasKey(entity => entity.Id);

        _ = builder.Property(entity => entity.RunId).IsRequired();
        _ = builder.Property(entity => entity.SourceFolder).IsRequired().HasMaxLength(PathMaxLength);
        _ = builder.Property(entity => entity.CategoryName).IsRequired().HasMaxLength(CategoryMaxLength);
        _ = builder.Property(entity => entity.ByDateOnly).IsRequired();
        _ = builder.Property(entity => entity.IncludeSubfolders).IsRequired();
        _ = builder.Property(entity => entity.State).IsRequired();
        _ = builder.Property(entity => entity.StartedAtUtc).IsRequired();
        _ = builder.Property(entity => entity.LastProgressAtUtc).IsRequired();
        _ = builder.Property(entity => entity.TotalPhotos).IsRequired();
        _ = builder.Property(entity => entity.FailureReason).HasMaxLength(ReasonMaxLength);

        _ = builder.HasIndex(entity => entity.RunId).IsUnique().HasDatabaseName("IX_AnalysisRun_RunId");

        // Der einzige Lesezugriff lautet „der jüngste Lauf".
        _ = builder
            .HasIndex(entity => entity.StartedAtUtc)
            .HasDatabaseName("IX_AnalysisRun_StartedAtUtc");

        // Ein Lauf ohne seine Ergebnisse wäre wertlos: Sie verschwinden mit ihm.
        _ = builder
            .HasMany(entity => entity.Items)
            .WithOne(item => item.AnalysisRun)
            .HasForeignKey(item => item.AnalysisRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
