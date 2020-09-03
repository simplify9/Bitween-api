using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Value",
                table: "XchangePromotedProperties");

            migrationBuilder.AlterColumn<DateTime>(
                name: "FinishedOn",
                table: "XchangeResults",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "FinishedOn",
                table: "XchangeResults",
                type: "datetime(6)",
                nullable: true,
                oldClrType: typeof(DateTime));

            migrationBuilder.AddColumn<string>(
                name: "Value",
                table: "XchangePromotedProperties",
                type: "longtext CHARACTER SET utf8mb4",
                nullable: true);
        }
    }
}
