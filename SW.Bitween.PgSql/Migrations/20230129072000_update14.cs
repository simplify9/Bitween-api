using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    public partial class update14 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "match_expression",
                schema: "Bitween",
                table: "subscription",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "Bitween",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999,
                column: "created_on",
                value: new DateTime(2021, 12, 31, 21, 0, 0, 0, DateTimeKind.Utc));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "match_expression",
                schema: "Bitween",
                table: "subscription");

            migrationBuilder.UpdateData(
                schema: "Bitween",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999,
                column: "created_on",
                value: new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc));
        }
    }
}
