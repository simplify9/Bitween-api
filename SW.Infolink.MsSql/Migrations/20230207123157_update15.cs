using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Infolink.MsSql.Migrations
{
    public partial class update15 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTrail",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<int>(type: "int", nullable: false),
                    StateBefore = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StateAfter = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTrail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentTrail_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionTrail",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubscriptionId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<int>(type: "int", nullable: false),
                    StateBefore = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StateAfter = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionTrail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionTrail_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 9999,
                column: "CreatedOn",
                value: new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTrail_CreatedOn",
                table: "DocumentTrail",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTrail_DocumentId",
                table: "DocumentTrail",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTrail_CreatedOn",
                table: "SubscriptionTrail",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTrail_SubscriptionId",
                table: "SubscriptionTrail",
                column: "SubscriptionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTrail");

            migrationBuilder.DropTable(
                name: "SubscriptionTrail");

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 9999,
                column: "CreatedOn",
                value: new DateTime(2021, 12, 31, 21, 0, 0, 0, DateTimeKind.Utc));
        }
    }
}
