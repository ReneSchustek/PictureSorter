using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureSorter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSortRunOperationAndTargetStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Größe und Änderungszeit der Zieldatei zum Zeitpunkt des Sortierens. Sie
            // belegen später, dass eine Kopie noch unverändert ist und gefahrlos
            // entfernt werden darf. Bestandszeilen bleiben leer – für sie unterbleibt
            // das Entfernen, und das ist die sichere Richtung.
            _ = migrationBuilder.AddColumn<DateTime>(
                name: "TargetLastWriteUtc",
                table: "SortRunItem",
                type: "TEXT",
                nullable: true);

            _ = migrationBuilder.AddColumn<long>(
                name: "TargetLength",
                table: "SortRunItem",
                type: "INTEGER",
                nullable: true);

            // 0 = Verschieben. Vor dieser Wahlmöglichkeit wurde ausnahmslos verschoben;
            // der Vorgabewert für Bestandszeilen ist also kein Notbehelf, sondern
            // zutreffend.
            _ = migrationBuilder.AddColumn<int>(
                name: "Operation",
                table: "SortRun",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropColumn(
                name: "TargetLastWriteUtc",
                table: "SortRunItem");

            _ = migrationBuilder.DropColumn(
                name: "TargetLength",
                table: "SortRunItem");

            _ = migrationBuilder.DropColumn(
                name: "Operation",
                table: "SortRun");
        }
    }
}
