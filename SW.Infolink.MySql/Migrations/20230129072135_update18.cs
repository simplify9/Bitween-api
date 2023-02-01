using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Infolink.MySql.Migrations
{
    public partial class update18 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MatchExpression",
                table: "Subscriptions",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 9999,
                column: "CreatedOn",
                value: new DateTime(2021, 12, 31, 21, 0, 0, 0, DateTimeKind.Utc));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchExpression",
                table: "Subscriptions");

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 9999,
                column: "CreatedOn",
                value: new DateTime(2022, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
