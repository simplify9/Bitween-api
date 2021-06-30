using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update8 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_notifiers",
                table: "notifiers");

            migrationBuilder.RenameTable(
                name: "notifiers",
                newName: "Notifiers");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Notifiers",
                table: "Notifiers",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "XchangeNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    XchangeId = table.Column<string>(unicode: false, maxLength: 50, nullable: true),
                    Success = table.Column<bool>(nullable: false),
                    NotifierId = table.Column<int>(nullable: false),
                    NotifierName = table.Column<string>(nullable: true),
                    Exception = table.Column<string>(nullable: true),
                    FinishedOn = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeNotifications", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XchangeNotifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Notifiers",
                table: "Notifiers");

            migrationBuilder.RenameTable(
                name: "Notifiers",
                newName: "notifiers");

            migrationBuilder.AddPrimaryKey(
                name: "PK_notifiers",
                table: "notifiers",
                column: "Id");
        }
    }
}
