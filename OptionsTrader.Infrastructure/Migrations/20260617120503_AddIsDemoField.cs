using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptionsTrader.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDemoField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDemo",
                table: "Trades",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$gnwnfJdUR5Mrg44dL3qQS.Agz1/bruhFp8DEf5PeIoOr2kMuHBj4u");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "$2a$11$73IqNpjiMG4EIK03loij3.AyDlUQ/AfA6FONLQOXV33gq7iS7YrG6");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "PasswordHash",
                value: "$2a$11$D6S3MvSKw/VitKmSh2AzmOSeaHRGRgEx7EPbjIIIU9vwPblRQMpGW");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                column: "PasswordHash",
                value: "$2a$11$4.53zCR9UUAXpHyqTruCiemdxWBRgaQ.Uy9BeNn4t3FK1L6mVbkTi");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 5,
                column: "PasswordHash",
                value: "$2a$11$f38j0wxSjkhtFEqQvwsOgeXU9nHD4Bv7h0avt7O4a2Vicj60Xkxum");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDemo",
                table: "Trades");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$dgDv.f7A33xAsaWPn99PUOR9nGr1BYnE8ZbCoEN.O5ND5B3fTN0zS");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "$2a$11$taLofYtpXX1XG.iRsxcbouKURNU0EktEu8NTqZBUlazx1jvp21Ngm");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "PasswordHash",
                value: "$2a$11$pm9IkQDUERzv06oAbDCRAuxG9CIHBuEb8riZag4/ye4.KD.c3e06i");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                column: "PasswordHash",
                value: "$2a$11$.XmU.jW8rviT7crJuwODCOU9S3f8kvUKo0eMfaIDrzJGWfQxh7hmy");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 5,
                column: "PasswordHash",
                value: "$2a$11$g0BVhNXTwYwLqSVY8WoWaumOe.ffdrT05eBn/7E9k7zprVcQXP/NK");
        }
    }
}
