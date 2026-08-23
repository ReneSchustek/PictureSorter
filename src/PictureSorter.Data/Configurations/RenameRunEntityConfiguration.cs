using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Configurations;

/// <summary>
/// Legt Tabelle, Pflichtfelder und Indizes des Umbenennungs-Laufs fest.
/// </summary>
internal sealed class RenameRunEntityConfiguration : IEntityTypeConfiguration<RenameRunEntity>
{
    private const int PathMaxLength = 1024;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RenameRunEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable("RenameRun");
        _ = builder.HasKey(entity => entity.Id);

        _ = builder.Property(entity => entity.RunId).IsRequired();
        _ = builder.Property(entity => entity.StartedAtUtc).IsRequired();
        _ = builder.Property(entity => entity.Folder).IsRequired().HasMaxLength(PathMaxLength);
        _ = builder.Property(entity => entity.IsUndone).IsRequired();

        _ = builder.HasIndex(entity => entity.RunId).IsUnique().HasDatabaseName("IX_RenameRun_RunId");

        // Der einzige Lesezugriff lautet „der jüngste noch nicht zurückgenommene Lauf".
        _ = builder
            .HasIndex(entity => new { entity.IsUndone, entity.StartedAtUtc })
            .HasDatabaseName("IX_RenameRun_IsUndone_StartedAtUtc");

        // Ein Lauf ohne seine Umbenennungen wäre wertlos: Sie verschwinden mit ihm.
        _ = builder
            .HasMany(entity => entity.Items)
            .WithOne(item => item.RenameRun)
            .HasForeignKey(item => item.RenameRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
