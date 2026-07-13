using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureSorter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSortRunJournal : Migration
    {
        // Der einzige Lesezugriff lautet „der jüngste noch nicht zurückgenommene Lauf".
        private static readonly string[] UndoLookupColumns = ["IsUndone", "StartedAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.CreateTable(
                name: "SortRun",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceFolder = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CategoryName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IsUndone = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_SortRun", x => x.Id);
                });

            _ = migrationBuilder.CreateTable(
                name: "SortRunItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SortRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    TargetPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileSignature = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_SortRunItem", x => x.Id);

                    // Ein Lauf ohne seine Verschiebungen wäre wertlos: Sie verschwinden mit ihm.
                    _ = table.ForeignKey(
                        name: "FK_SortRunItem_SortRun_SortRunId",
                        column: x => x.SortRunId,
                        principalTable: "SortRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_SortRun_IsUndone_StartedAtUtc",
                table: "SortRun",
                columns: UndoLookupColumns);

            _ = migrationBuilder.CreateIndex(
                name: "IX_SortRun_RunId",
                table: "SortRun",
                column: "RunId",
                unique: true);

            _ = migrationBuilder.CreateIndex(
                name: "IX_SortRunItem_SortRunId",
                table: "SortRunItem",
                column: "SortRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "SortRunItem");

            _ = migrationBuilder.DropTable(
                name: "SortRun");
        }
    }
}
