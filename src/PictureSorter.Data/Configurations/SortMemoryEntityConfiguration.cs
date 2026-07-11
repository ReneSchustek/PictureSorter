using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureSorter.Data.Entities;

namespace PictureSorter.Data.Configurations;

/// <summary>
/// Legt Tabelle, Pflichtfelder und Indizes des Gedächtnis-Eintrags fest.
/// </summary>
internal sealed class SortMemoryEntityConfiguration : IEntityTypeConfiguration<SortMemoryEntity>
{
    // Pfade können lang werden; die Grenze verhindert unbegrenzte Werte, ohne
    // realistische Windows-Pfade (max. 260 bzw. 32.767 Zeichen) abzuschneiden.
    private const int PathMaxLength = 1024;
    private const int SignatureMaxLength = 128;
    private const int CategoryMaxLength = 256;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SortMemoryEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.ToTable("SortMemory");
        _ = builder.HasKey(entity => entity.Id);

        _ = builder.Property(entity => entity.FolderPath).IsRequired().HasMaxLength(PathMaxLength);
        _ = builder.Property(entity => entity.FileSignature).IsRequired().HasMaxLength(SignatureMaxLength);
        _ = builder.Property(entity => entity.PhotoPath).IsRequired().HasMaxLength(PathMaxLength);
        _ = builder.Property(entity => entity.CategoryName).IsRequired().HasMaxLength(CategoryMaxLength);
        _ = builder.Property(entity => entity.Status).IsRequired();
        _ = builder.Property(entity => entity.Confidence).IsRequired();
        _ = builder.Property(entity => entity.UpdatedAtUtc).IsRequired();

        // Je Ordner, Datei und Kategorie genau eine Entscheidung. Die Kategorie
        // gehört dazu, weil dasselbe Foto für „Familie" und „Urlaub" verschieden
        // beurteilt werden kann. Der Index macht den Upsert idempotent.
        _ = builder
            .HasIndex(entity => new { entity.FolderPath, entity.FileSignature, entity.CategoryName })
            .IsUnique()
            .HasDatabaseName("IX_SortMemory_Folder_Signature_Category");

        // Der häufigste Zugriff ist „alle Einträge eines Ordners".
        _ = builder
            .HasIndex(entity => entity.FolderPath)
            .HasDatabaseName("IX_SortMemory_Folder");
    }
}
