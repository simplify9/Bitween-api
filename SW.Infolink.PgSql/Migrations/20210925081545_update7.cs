using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.PgSql.Migrations
{
    public partial class update7 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "run_on_subscriptions",
                schema: "infolink",
                table: "notifier",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "run_on_subscriptions",
                schema: "infolink",
                table: "notifier");
        }
    }
}
