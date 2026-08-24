using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class FixDefaultCreatedOnTimezone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999,
                column: "created_on",
                value: new DateTime(2022, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 1,
                column: "created_on",
                value: new DateTime(2022, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 2,
                column: "created_on",
                value: new DateTime(2022, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 3,
                column: "created_on",
                value: new DateTime(2022, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999,
                column: "created_on",
                value: new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 1,
                column: "created_on",
                value: new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 2,
                column: "created_on",
                value: new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 3,
                column: "created_on",
                value: new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc));
        }
    }
}
