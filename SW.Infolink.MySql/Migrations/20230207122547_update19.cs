using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Infolink.MySql.Migrations
{
    public partial class update19 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTrail",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<int>(type: "int", nullable: false),
                    StateBefore = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StateAfter = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOn = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SubscriptionTrail",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubscriptionId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<int>(type: "int", nullable: false),
                    StateBefore = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StateAfter = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOn = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
