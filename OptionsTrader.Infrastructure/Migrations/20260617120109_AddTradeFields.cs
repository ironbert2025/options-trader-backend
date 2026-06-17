using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptionsTrader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Contracts",
                table: "Trades",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "Duration",
                table: "Trades",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EntryTime",
                table: "Trades",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "Trades",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Pnl",
                table: "Trades",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PnlPercent",
                table: "Trades",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetPercent",
                table: "Trades",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$yC0FPNRVNuCnx4QXzOvwxudPB.ELdE1JCgA1XXq.uUMVJaQHRS1Mu");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "$2a$11$WutdB2SqpkGVYUfgwq.RB.oflAva/BvO7OrZRbgsSn6Cz49nkI9g.");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "PasswordHash",
                value: "$2a$11$NDZssu4Cwl2qvCNmli3s1e57tQdd4xn0K4.o1fUA6qytE6VdUZkuq");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                column: "PasswordHash",
                value: "$2a$11$l8Swuuidp.DeWvuIsafyYuMCrfP.gPpXKh/cIurS4kSWgS3kd3gMK");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 5,
                column: "PasswordHash",
                value: "$2a$11$7z8lMqzqES6r5nKDyWjq1.2eFmVDUcOzy0xtnVq9F6b94LYlbT.jm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Contracts",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "Duration",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "EntryTime",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "Pnl",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "PnlPercent",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "TargetPercent",
                table: "Trades");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$VfBtEy8nlQhMMBJI82eiXu8TSfVBeBpyKSdBFJ9zA2/zGxwdfd5sq");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "$2a$11$7TFKfOFqT8ufmMOmJ3dvEuTB4cXMI8UiGRrWmCWsZjCEnr49nEJnK");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "PasswordHash",
                value: "$2a$11$yBnnmhN6rs3nvSKfQ.feiuE63fXJJ6CaM7ZjW1doOLS.GLeovvi7m");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                column: "PasswordHash",
                value: "$2a$11$YB.H.lxUuJkCZ8FNZK1YY.rfr.X1MgDTNr7WjdDqzhpAIA1.tymym");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 5,
                column: "PasswordHash",
                value: "$2a$11$C2sl.yuQLroPI.1MhHhSFuYQS1MkgnFMbauq1viXPGdxDL2IE96B6");
        }
    }
}
