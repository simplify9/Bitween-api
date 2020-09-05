using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update5 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "On",
                table: "SubscriptionSchedules",
                nullable: false,
                oldClrType: typeof(TimeSpan),
                oldType: "time(6)");

            migrationBuilder.AlterColumn<double>(
                name: "On",
                table: "SubscriptionReceiveSchedules",
                nullable: false,
                oldClrType: typeof(TimeSpan),
                oldType: "time(6)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<TimeSpan>(
                name: "On",
                table: "SubscriptionSchedules",
                type: "time(6)",
                nullable: false,
                oldClrType: typeof(double));

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "On",
                table: "SubscriptionReceiveSchedules",
                type: "time(6)",
                nullable: false,
                oldClrType: typeof(double));
        }
    }
}
