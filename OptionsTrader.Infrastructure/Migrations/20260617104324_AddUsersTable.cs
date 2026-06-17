using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OptionsTrader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "PasswordHash", "Username" },
                values: new object[,]
                {
                    { 1, "$2a$11$VfBtEy8nlQhMMBJI82eiXu8TSfVBeBpyKSdBFJ9zA2/zGxwdfd5sq", "user1" },
                    { 2, "$2a$11$7TFKfOFqT8ufmMOmJ3dvEuTB4cXMI8UiGRrWmCWsZjCEnr49nEJnK", "user2" },
                    { 3, "$2a$11$yBnnmhN6rs3nvSKfQ.feiuE63fXJJ6CaM7ZjW1doOLS.GLeovvi7m", "user3" },
                    { 4, "$2a$11$YB.H.lxUuJkCZ8FNZK1YY.rfr.X1MgDTNr7WjdDqzhpAIA1.tymym", "user4" },
                    { 5, "$2a$11$C2sl.yuQLroPI.1MhHhSFuYQS1MkgnFMbauq1viXPGdxDL2IE96B6", "user5" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
