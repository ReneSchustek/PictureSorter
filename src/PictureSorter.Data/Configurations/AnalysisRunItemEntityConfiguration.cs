using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Configurations;

/// <summary>
/// Legt Tabelle, Pflichtfelder und Indizes eines protokollierten Ergebnisses fest.
/// </summary>
internal sealed class AnalysisRunItemEntityConfiguration : IEntityTypeConfiguration<AnalysisRunItemEntity>
{
    private const int PathMaxLength = 1024;
    private const int SignatureMaxLength = 128;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AnalysisRunItemEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable("AnalysisRunItem");
        _ = builder.HasKey(entity => entity.Id);

        _ = builder.Property(entity => entity.FileSignature).IsRequired().HasMaxLength(SignatureMaxLength);
        _ = builder.Property(entity => entity.PhotoPath).IsRequired().HasMaxLength(PathMaxLength);
        _ = builder.Property(entity => entity.Outcome).IsRequired();
        _ = builder.Property(entity => entity.Confidence).IsRequired();
        _ = builder.Property(entity => entity.Method).IsRequired();
        _ = builder.Property(entity => entity.DecidedAtUtc).IsRequired();

        // Beim Fortsetzen wird zu jedem Lauf genau einmal die Menge der bereits
        // entschiedenen Signaturen gelesen; ohne diesen Index wäre das ein Tabellenscan
        // über hunderttausende Zeilen.
        _ = builder
            .HasIndex(entity => new { entity.AnalysisRunId, entity.FileSignature })
            .HasDatabaseName("IX_AnalysisRunItem_AnalysisRunId_FileSignature");
    }
}
