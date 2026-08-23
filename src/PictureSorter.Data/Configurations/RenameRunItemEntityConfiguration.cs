using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Configurations;

/// <summary>
/// Legt Tabelle und Pflichtfelder einer einzelnen Umbenennung fest.
/// </summary>
internal sealed class RenameRunItemEntityConfiguration : IEntityTypeConfiguration<RenameRunItemEntity>
{
    private const int PathMaxLength = 1024;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RenameRunItemEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable("RenameRunItem");
        _ = builder.HasKey(entity => entity.Id);

        _ = builder.Property(entity => entity.OriginalPath).IsRequired().HasMaxLength(PathMaxLength);
        _ = builder.Property(entity => entity.NewPath).IsRequired().HasMaxLength(PathMaxLength);

        _ = builder.HasIndex(entity => entity.RenameRunId).HasDatabaseName("IX_RenameRunItem_RenameRunId");
    }
}
