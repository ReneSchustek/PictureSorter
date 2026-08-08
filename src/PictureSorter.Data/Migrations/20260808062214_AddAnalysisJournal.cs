using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureSorter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisJournal : Migration
    {
        // Beim Fortsetzen wird je Lauf einmal die Menge der bereits entschiedenen
        // Signaturen gelesen; ohne diesen Index wäre das ein Tabellenscan.
        private static readonly string[] ResumeLookupColumns = ["AnalysisRunId", "FileSignature"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.CreateTable(
                name: "AnalysisRun",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceFolder = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CategoryName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ByDateOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeSubfolders = table.Column<bool>(type: "INTEGER", nullable: false),
                    RangeFrom = table.Column<int>(type: "INTEGER", nullable: true),
                    RangeTo = table.Column<int>(type: "INTEGER", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastProgressAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalPhotos = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_AnalysisRun", x => x.Id);
                });

            _ = migrationBuilder.CreateTable(
                name: "AnalysisRunItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AnalysisRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileSignature = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PhotoPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_AnalysisRunItem", x => x.Id);
                    _ = table.ForeignKey(
                        name: "FK_AnalysisRunItem_AnalysisRun_AnalysisRunId",
                        column: x => x.AnalysisRunId,
                        principalTable: "AnalysisRun",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_AnalysisRun_RunId",
                table: "AnalysisRun",
                column: "RunId",
                unique: true);

            _ = migrationBuilder.CreateIndex(
                name: "IX_AnalysisRun_StartedAtUtc",
                table: "AnalysisRun",
                column: "StartedAtUtc");

            _ = migrationBuilder.CreateIndex(
                name: "IX_AnalysisRunItem_AnalysisRunId_FileSignature",
                table: "AnalysisRunItem",
                columns: ResumeLookupColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "AnalysisRunItem");

            _ = migrationBuilder.DropTable(
                name: "AnalysisRun");
        }
    }
}
