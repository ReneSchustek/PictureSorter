using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Configurations;

/// <summary>
/// Legt Tabelle und Pflichtfelder einer einzelnen Verschiebung fest.
/// </summary>
internal sealed class SortRunItemEntityConfiguration : IEntityTypeConfiguration<SortRunItemEntity>
{
    // Pfade können lang werden; die Grenze verhindert unbegrenzte Werte, ohne realistische
    // Windows-Pfade abzuschneiden. Teil des Schemas: Eine Änderung braucht eine Migration.
    private const int PathMaxLength = 1024;
    // Eine Signatur ist ein SHA-256 in Hexadezimalschreibweise, also 64 Zeichen. Der Rest ist Reserve,
    // weil eine Änderung des Schemas eine Migration braucht.
    private const int SignatureMaxLength = 128;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SortRunItemEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable("SortRunItem");
        _ = builder.HasKey(entity => entity.Id);

        _ = builder.Property(entity => entity.SourcePath).IsRequired().HasMaxLength(PathMaxLength);
        _ = builder.Property(entity => entity.TargetPath).IsRequired().HasMaxLength(PathMaxLength);
        _ = builder.Property(entity => entity.FileSignature).IsRequired().HasMaxLength(SignatureMaxLength);

        _ = builder.HasIndex(entity => entity.SortRunId).HasDatabaseName("IX_SortRunItem_SortRunId");
    }
}
