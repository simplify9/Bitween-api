using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class AdapterUpdate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Hash",
                table: "Adapters");

            migrationBuilder.DropColumn(
                name: "Package",
                table: "Adapters");

            migrationBuilder.AddColumn<string>(
                name: "ServerlessId",
                table: "Adapters",
                unicode: false,
                maxLength: 200,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServerlessId",
                table: "Adapters");

            migrationBuilder.AddColumn<string>(
                name: "Hash",
                table: "Adapters",
                type: "varchar(50) CHARACTER SET utf8mb4",
                unicode: false,
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "Package",
                table: "Adapters",
                type: "longblob",
                nullable: true);
        }
    }
}
