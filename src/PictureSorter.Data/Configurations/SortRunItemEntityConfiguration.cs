using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Configurations;

/// <summary>
/// Legt Tabelle und Pflichtfelder einer einzelnen Verschiebung fest.
/// </summary>
internal sealed class SortRunItemEntityConfiguration : IEntityTypeConfiguration<SortRunItemEntity>
{
    private const int PathMaxLength = 1024;
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
