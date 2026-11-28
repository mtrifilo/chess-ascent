using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessAscent.Api.Database.Migrations;

/// <inheritdoc />
public partial class _20261128022216_AddHintCount : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // LEARN: Both columns are non-nullable ints with no default in the
        // model, since EF Core writes every value itself on insert. The
        // Games table may already have rows, though, and they need a value
        // too, so `dotnet ef migrations add` gave each column defaultValue: 0
        // (the int's default), which becomes
        // ALTER TABLE "Games" ADD "HintCount" integer NOT NULL DEFAULT 0.
        // With Flyway that statement is yours to write; Hibernate's
        // hbm2ddl.auto=update would add the column without a default, which
        // a table with rows refuses for a NOT NULL column. Since PostgreSQL
        // 11, adding a column with a constant default changes only the
        // catalog: existing rows are not rewritten, and read as 0.
        migrationBuilder.AddColumn<int>(
            name: "HintCount",
            table: "Games",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "LastHintedHalfMoveNumber",
            table: "Games",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "HintCount",
            table: "Games");

        migrationBuilder.DropColumn(
            name: "LastHintedHalfMoveNumber",
            table: "Games");
    }
}
