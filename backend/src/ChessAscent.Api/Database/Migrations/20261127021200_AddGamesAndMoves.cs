using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessAscent.Api.Database.Migrations;

/// <inheritdoc />
public partial class _20261127021200_AddGamesAndMoves : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Games",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<string>(type: "text", nullable: false),
                PlayerSide = table.Column<string>(type: "text", nullable: false),
                OpponentKind = table.Column<string>(type: "text", nullable: false),
                OpponentStrength = table.Column<int>(type: "integer", nullable: true),
                StartingFen = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                Result = table.Column<string>(type: "text", nullable: true),
                EndReason = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Games", x => x.Id);
                table.ForeignKey(
                    name: "FK_Games_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Moves",
            columns: table => new
            {
                GameId = table.Column<Guid>(type: "uuid", nullable: false),
                HalfMoveNumber = table.Column<int>(type: "integer", nullable: false),
                Uci = table.Column<string>(type: "text", nullable: false),
                San = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Moves", x => new { x.GameId, x.HalfMoveNumber });
                table.ForeignKey(
                    name: "FK_Moves_Games_GameId",
                    column: x => x.GameId,
                    principalTable: "Games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Games_OwnerId_UpdatedAt",
            table: "Games",
            columns: new[] { "OwnerId", "UpdatedAt" },
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Moves");

        migrationBuilder.DropTable(
            name: "Games");
    }
}
