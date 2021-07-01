using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update9 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PausedOn",
                table: "Subscriptions",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OnHoldXchanges",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SubscriptionId = table.Column<int>(nullable: false),
                    References = table.Column<string>(maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnHoldXchanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnHoldXchanges_SubscriptionId",
                table: "OnHoldXchanges",
                column: "SubscriptionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnHoldXchanges");

            migrationBuilder.DropColumn(
                name: "PausedOn",
                table: "Subscriptions");
        }
    }
}
