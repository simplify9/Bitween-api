using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Infolink.PgSql.Migrations
{
    public partial class DraftSubs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_draft",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<int>(type: "integer", nullable: false),
                    subscription_id = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: true),
                    validator_id = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: true),
                    handler_id = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: true),
                    receiver_id = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: true),
                    mapper_id = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: true),
                    validator_properties = table.Column<string>(type: "text", nullable: true),
                    handler_properties = table.Column<string>(type: "text", nullable: true),
                    mapper_properties = table.Column<string>(type: "text", nullable: true),
                    receiver_properties = table.Column<string>(type: "text", nullable: true),
                    document_filter = table.Column<string>(type: "text", nullable: true),
                    match_expression = table.Column<string>(type: "text", nullable: true),
                    response_subscription_id = table.Column<int>(type: "integer", nullable: true),
                    response_message_type_name = table.Column<string>(type: "character varying(500)", unicode: false, maxLength: 500, nullable: true),
                    published_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    modified_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_draft", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_draft_subscription_category_category_id",
                        column: x => x.category_id,
                        principalSchema: "infolink",
                        principalTable: "subscription_category",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_subscription_draft_subscription_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "infolink",
                        principalTable: "subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Subscriptions_RespSub",
                        column: x => x.response_subscription_id,
                        principalSchema: "infolink",
                        principalTable: "subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "draft_subscription_schedule",
                schema: "infolink",
                columns: table => new
                {
                    subscription_draft_id = table.Column<int>(type: "integer", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    recurrence = table.Column<byte>(type: "smallint", nullable: false),
                    on = table.Column<long>(type: "bigint", nullable: false),
                    backwards = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_draft_subscription_schedule", x => new { x.subscription_draft_id, x.id });
                    table.ForeignKey(
                        name: "fk_draft_subscription_schedule_subscription_draft_subscription",
                        column: x => x.subscription_draft_id,
                        principalSchema: "infolink",
                        principalTable: "subscription_draft",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_draft_category_id",
                schema: "infolink",
                table: "subscription_draft",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_draft_response_subscription_id",
                schema: "infolink",
                table: "subscription_draft",
                column: "response_subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_draft_subscription_id",
                schema: "infolink",
                table: "subscription_draft",
                column: "subscription_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "draft_subscription_schedule",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "subscription_draft",
                schema: "infolink");
        }
    }
}
