using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureSorter.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        // Eindeutig je Ordner, Datei und Kategorie: dasselbe Foto darf für
        // verschiedene Kategorien unterschiedlich beurteilt werden, für dieselbe
        // Kategorie aber nur einmal. Das macht den Upsert idempotent.
        private static readonly string[] UniqueDecisionColumns =
            ["FolderPath", "FileSignature", "CategoryName"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.CreateTable(
                name: "SortMemory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FolderPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileSignature = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PhotoPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CategoryName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_SortMemory", x => x.Id);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_SortMemory_Folder",
                table: "SortMemory",
                column: "FolderPath");

            _ = migrationBuilder.CreateIndex(
                name: "IX_SortMemory_Folder_Signature_Category",
                table: "SortMemory",
                columns: UniqueDecisionColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "SortMemory");
        }
    }
}
