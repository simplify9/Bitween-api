using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReceiverSchedules_Receivers_ReceiverId",
                table: "ReceiverSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscribers_Partners_PartnerId",
                table: "Subscribers");

            migrationBuilder.DropForeignKey(
                name: "FK_SubscriberSchedules_Subscribers_SubscriptionId",
                table: "SubscriberSchedules");

            migrationBuilder.DropTable(
                name: "Receivers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubscriberSchedules",
                table: "SubscriberSchedules");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReceiverSchedules",
                table: "ReceiverSchedules");

            migrationBuilder.DropIndex(
                name: "IX_ReceiverSchedules_ReceiverId",
                table: "ReceiverSchedules");

            migrationBuilder.DropColumn(
                name: "ReceiverId",
                table: "ReceiverSchedules");

            migrationBuilder.RenameTable(
                name: "SubscriberSchedules",
                newName: "SubscriptionSchedules");

            migrationBuilder.RenameTable(
                name: "ReceiverSchedules",
                newName: "SubscriptionReceiveSchedules");

            migrationBuilder.RenameIndex(
                name: "IX_SubscriberSchedules_SubscriptionId",
                table: "SubscriptionSchedules",
                newName: "IX_SubscriptionSchedules_SubscriptionId");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceiveOn",
                table: "Subscribers",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiverId",
                table: "Subscribers",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiverProperties",
                table: "Subscribers",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Type",
                table: "Subscribers",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionId",
                table: "SubscriptionReceiveSchedules",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubscriptionSchedules",
                table: "SubscriptionSchedules",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubscriptionReceiveSchedules",
                table: "SubscriptionReceiveSchedules",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BusMessageTypeName",
                table: "Documents",
                column: "BusMessageTypeName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionReceiveSchedules_SubscriptionId",
                table: "SubscriptionReceiveSchedules",
                column: "SubscriptionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subscribers_Partners_PartnerId",
                table: "Subscribers",
                column: "PartnerId",
                principalTable: "Partners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionReceiveSchedules_Subscribers_SubscriptionId",
                table: "SubscriptionReceiveSchedules",
                column: "SubscriptionId",
                principalTable: "Subscribers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionSchedules_Subscribers_SubscriptionId",
                table: "SubscriptionSchedules",
                column: "SubscriptionId",
                principalTable: "Subscribers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subscribers_Partners_PartnerId",
                table: "Subscribers");

            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionReceiveSchedules_Subscribers_SubscriptionId",
                table: "SubscriptionReceiveSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionSchedules_Subscribers_SubscriptionId",
                table: "SubscriptionSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Documents_BusMessageTypeName",
                table: "Documents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubscriptionSchedules",
                table: "SubscriptionSchedules");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubscriptionReceiveSchedules",
                table: "SubscriptionReceiveSchedules");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionReceiveSchedules_SubscriptionId",
                table: "SubscriptionReceiveSchedules");

            migrationBuilder.DropColumn(
                name: "ReceiveOn",
                table: "Subscribers");

            migrationBuilder.DropColumn(
                name: "ReceiverId",
                table: "Subscribers");

            migrationBuilder.DropColumn(
                name: "ReceiverProperties",
                table: "Subscribers");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Subscribers");

            migrationBuilder.DropColumn(
                name: "SubscriptionId",
                table: "SubscriptionReceiveSchedules");

            migrationBuilder.RenameTable(
                name: "SubscriptionSchedules",
                newName: "SubscriberSchedules");

            migrationBuilder.RenameTable(
                name: "SubscriptionReceiveSchedules",
                newName: "ReceiverSchedules");

            migrationBuilder.RenameIndex(
                name: "IX_SubscriptionSchedules_SubscriptionId",
                table: "SubscriberSchedules",
                newName: "IX_SubscriberSchedules_SubscriptionId");

            migrationBuilder.AddColumn<int>(
                name: "ReceiverId",
                table: "ReceiverSchedules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubscriberSchedules",
                table: "SubscriberSchedules",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReceiverSchedules",
                table: "ReceiverSchedules",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "Receivers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100) CHARACTER SET utf8mb4", maxLength: 100, nullable: false),
                    Properties = table.Column<string>(type: "longtext CHARACTER SET utf8mb4", nullable: true),
                    ReceiveOn = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReceiverId = table.Column<string>(type: "varchar(200) CHARACTER SET utf8mb4", unicode: false, maxLength: 200, nullable: false),
                    SubscriberId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Receivers_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiverSchedules_ReceiverId",
                table: "ReceiverSchedules",
                column: "ReceiverId");

            migrationBuilder.CreateIndex(
                name: "IX_Receivers_SubscriberId",
                table: "Receivers",
                column: "SubscriberId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiverSchedules_Receivers_ReceiverId",
                table: "ReceiverSchedules",
                column: "ReceiverId",
                principalTable: "Receivers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Subscribers_Partners_PartnerId",
                table: "Subscribers",
                column: "PartnerId",
                principalTable: "Partners",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriberSchedules_Subscribers_SubscriptionId",
                table: "SubscriberSchedules",
                column: "SubscriptionId",
                principalTable: "Subscribers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
