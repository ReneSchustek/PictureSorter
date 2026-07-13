using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Configurations;

/// <summary>
/// Legt Tabelle, Pflichtfelder und Indizes des Sortierlaufs fest.
/// </summary>
internal sealed class SortRunEntityConfiguration : IEntityTypeConfiguration<SortRunEntity>
{
    private const int PathMaxLength = 1024;
    private const int CategoryMaxLength = 256;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SortRunEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable("SortRun");
        _ = builder.HasKey(entity => entity.Id);

        _ = builder.Property(entity => entity.RunId).IsRequired();
        _ = builder.Property(entity => entity.StartedAtUtc).IsRequired();
        _ = builder.Property(entity => entity.SourceFolder).IsRequired().HasMaxLength(PathMaxLength);
        _ = builder.Property(entity => entity.CategoryName).IsRequired().HasMaxLength(CategoryMaxLength);
        _ = builder.Property(entity => entity.IsUndone).IsRequired();

        _ = builder.HasIndex(entity => entity.RunId).IsUnique().HasDatabaseName("IX_SortRun_RunId");

        // Der einzige Lesezugriff lautet „der jüngste noch nicht zurückgenommene Lauf".
        _ = builder
            .HasIndex(entity => new { entity.IsUndone, entity.StartedAtUtc })
            .HasDatabaseName("IX_SortRun_IsUndone_StartedAtUtc");

        // Ein Lauf ohne seine Verschiebungen wäre wertlos: Sie verschwinden mit ihm.
        _ = builder
            .HasMany(entity => entity.Items)
            .WithOne(item => item.SortRun)
            .HasForeignKey(item => item.SortRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
