using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptionsTrader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToTrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add nullable first so existing rows aren't broken.
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Trades",
                type: "int",
                nullable: true);

            // Backfill every pre-existing trade to user1.
            migrationBuilder.Sql(
                "UPDATE Trades SET UserId = (SELECT Id FROM Users WHERE Username = 'user1') WHERE UserId IS NULL;");

            // Now that every row has a value, enforce NOT NULL + the FK.
            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Trades",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trades_UserId",
                table: "Trades",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Trades_Users_UserId",
                table: "Trades",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trades_Users_UserId",
                table: "Trades");

            migrationBuilder.DropIndex(
                name: "IX_Trades_UserId",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Trades");
        }
    }
}
