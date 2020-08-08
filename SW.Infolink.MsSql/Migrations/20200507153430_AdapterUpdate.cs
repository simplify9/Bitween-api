using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MsSql.Migrations
{
    public partial class AdapterUpdate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Hash",
                schema: "infolink",
                table: "Adapters");

            migrationBuilder.DropColumn(
                name: "Package",
                schema: "infolink",
                table: "Adapters");

            migrationBuilder.AddColumn<string>(
                name: "ServerlessId",
                schema: "infolink",
                table: "Adapters",
                unicode: false,
                maxLength: 200,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServerlessId",
                schema: "infolink",
                table: "Adapters");

            migrationBuilder.AddColumn<string>(
                name: "Hash",
                schema: "infolink",
                table: "Adapters",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "Package",
                schema: "infolink",
                table: "Adapters",
                type: "varbinary(max)",
                nullable: true);
        }
    }
}
