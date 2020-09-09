using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionSchedules_Subscribers_SubscriptionId",
                table: "SubscriptionSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Xchanges_DeliverOn",
                table: "Xchanges");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubscriptionSchedules",
                table: "SubscriptionSchedules");

            migrationBuilder.DropColumn(
                name: "DeliverOn",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "ReceiveConsecutiveFailures",
                table: "Subscribers");

            migrationBuilder.DropColumn(
                name: "ReceiveLastException",
                table: "Subscribers");

            migrationBuilder.RenameTable(
                name: "SubscriptionSchedules",
                newName: "SubscriptionAggregationSchedules");

            migrationBuilder.RenameIndex(
                name: "IX_SubscriptionSchedules_SubscriptionId",
                table: "SubscriptionAggregationSchedules",
                newName: "IX_SubscriptionAggregationSchedules_SubscriptionId");

            migrationBuilder.AlterColumn<string>(
                name: "ValidatorProperties",
                table: "Subscribers",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(200) CHARACTER SET utf8mb4",
                oldUnicode: false,
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ValidatorId",
                table: "Subscribers",
                unicode: false,
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext CHARACTER SET utf8mb4",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AggregateOn",
                table: "Subscribers",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "AggregationTarget",
                table: "Subscribers",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "Subscribers",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastException",
                table: "Subscribers",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubscriptionAggregationSchedules",
                table: "SubscriptionAggregationSchedules",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionAggregationSchedules_Subscribers_SubscriptionId",
                table: "SubscriptionAggregationSchedules",
                column: "SubscriptionId",
                principalTable: "Subscribers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionAggregationSchedules_Subscribers_SubscriptionId",
                table: "SubscriptionAggregationSchedules");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubscriptionAggregationSchedules",
                table: "SubscriptionAggregationSchedules");

            migrationBuilder.DropColumn(
                name: "AggregateOn",
                table: "Subscribers");

            migrationBuilder.DropColumn(
                name: "AggregationTarget",
                table: "Subscribers");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "Subscribers");

            migrationBuilder.DropColumn(
                name: "LastException",
                table: "Subscribers");

            migrationBuilder.RenameTable(
                name: "SubscriptionAggregationSchedules",
                newName: "SubscriptionSchedules");

            migrationBuilder.RenameIndex(
                name: "IX_SubscriptionAggregationSchedules_SubscriptionId",
                table: "SubscriptionSchedules",
                newName: "IX_SubscriptionSchedules_SubscriptionId");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliverOn",
                table: "Xchanges",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ValidatorProperties",
                table: "Subscribers",
                type: "varchar(200) CHARACTER SET utf8mb4",
                unicode: false,
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ValidatorId",
                table: "Subscribers",
                type: "longtext CHARACTER SET utf8mb4",
                nullable: true,
                oldClrType: typeof(string),
                oldUnicode: false,
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceiveConsecutiveFailures",
                table: "Subscribers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReceiveLastException",
                table: "Subscribers",
                type: "longtext CHARACTER SET utf8mb4",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubscriptionSchedules",
                table: "SubscriptionSchedules",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_DeliverOn",
                table: "Xchanges",
                column: "DeliverOn");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionSchedules_Subscribers_SubscriptionId",
                table: "SubscriptionSchedules",
                column: "SubscriptionId",
                principalTable: "Subscribers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
