using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Infolink.MsSql.Migrations
{
    public partial class SubscriptionDraft : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    SubscriptionId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    ValidatorId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    HandlerId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    ReceiverId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    MapperId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    ValidatorProperties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HandlerProperties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MapperProperties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceiverProperties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DocumentFilter = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MatchExpression = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    ResponseMessageTypeName = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    PublishedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionDrafts_SubscriptionCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "SubscriptionCategory",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubscriptionDrafts_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Subscriptions_RespSub",
                        column: x => x.ResponseSubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DraftSubscriptionSchedules",
                columns: table => new
                {
                    SubscriptionDraftId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Recurrence = table.Column<byte>(type: "tinyint", nullable: false),
                    On = table.Column<long>(type: "bigint", nullable: false),
                    Backwards = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftSubscriptionSchedules", x => new { x.SubscriptionDraftId, x.Id });
                    table.ForeignKey(
                        name: "FK_DraftSubscriptionSchedules_SubscriptionDrafts_SubscriptionDraftId",
                        column: x => x.SubscriptionDraftId,
                        principalTable: "SubscriptionDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDrafts_CategoryId",
                table: "SubscriptionDrafts",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDrafts_ResponseSubscriptionId",
                table: "SubscriptionDrafts",
                column: "ResponseSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDrafts_SubscriptionId",
                table: "SubscriptionDrafts",
                column: "SubscriptionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DraftSubscriptionSchedules");

            migrationBuilder.DropTable(
                name: "SubscriptionDrafts");
        }
    }
}
