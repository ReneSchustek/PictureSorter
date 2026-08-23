using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureSorter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRenameJournal : Migration
    {
        // Gesucht wird stets „der jüngste noch nicht zurückgenommene Lauf"; ohne
        // diesen Index wäre das ein Tabellenscan.
        private static readonly string[] LastUndoableColumns = ["IsUndone", "StartedAtUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.CreateTable(
                name: "RenameRun",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Folder = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    IsUndone = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_RenameRun", x => x.Id);
                });

            _ = migrationBuilder.CreateTable(
                name: "RenameRunItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RenameRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    NewPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_RenameRunItem", x => x.Id);
                    _ = table.ForeignKey(
                        name: "FK_RenameRunItem_RenameRun_RenameRunId",
                        column: x => x.RenameRunId,
                        principalTable: "RenameRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_RenameRun_IsUndone_StartedAtUtc",
                table: "RenameRun",
                columns: LastUndoableColumns);

            _ = migrationBuilder.CreateIndex(
                name: "IX_RenameRun_RunId",
                table: "RenameRun",
                column: "RunId",
                unique: true);

            _ = migrationBuilder.CreateIndex(
                name: "IX_RenameRunItem_RenameRunId",
                table: "RenameRunItem",
                column: "RenameRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "RenameRunItem");

            _ = migrationBuilder.DropTable(
                name: "RenameRun");
        }
    }
}
