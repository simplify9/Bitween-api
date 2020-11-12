using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace SW.Infolink.PgSql.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "infolink");

            migrationBuilder.CreateTable(
                name: "document",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false),
                    name = table.Column<string>(maxLength: 100, nullable: false),
                    bus_enabled = table.Column<bool>(nullable: false),
                    bus_message_type_name = table.Column<string>(maxLength: 500, nullable: true),
                    duplicate_interval = table.Column<int>(nullable: false),
                    promoted_properties = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partner",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "xchange",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<string>(maxLength: 50, nullable: false),
                    subscription_id = table.Column<int>(nullable: true),
                    document_id = table.Column<int>(nullable: false),
                    handler_id = table.Column<string>(maxLength: 200, nullable: true),
                    mapper_id = table.Column<string>(maxLength: 200, nullable: true),
                    handler_properties = table.Column<IReadOnlyDictionary<string, string>>(type: "jsonb", nullable: true),
                    mapper_properties = table.Column<IReadOnlyDictionary<string, string>>(type: "jsonb", nullable: true),
                    references = table.Column<string[]>(nullable: true),
                    started_on = table.Column<DateTime>(nullable: false),
                    input_name = table.Column<string>(maxLength: 200, nullable: true),
                    input_size = table.Column<int>(nullable: false),
                    input_hash = table.Column<string>(maxLength: 50, nullable: false),
                    input_content_type = table.Column<string>(maxLength: 200, nullable: true),
                    response_subscription_id = table.Column<int>(nullable: true),
                    response_message_type_name = table.Column<string>(maxLength: 500, nullable: true),
                    retry_for = table.Column<string>(maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xchange", x => x.id);
                    table.ForeignKey(
                        name: "fk_xchange_document_document_id",
                        column: x => x.document_id,
                        principalSchema: "infolink",
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "partner_api_credential",
                schema: "infolink",
                columns: table => new
                {
                    partner_id = table.Column<int>(nullable: false),
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(maxLength: 500, nullable: false),
                    key = table.Column<string>(maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_credential", x => new { x.partner_id, x.id });
                    table.ForeignKey(
                        name: "fk_api_credential_partner_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "infolink",
                        principalTable: "partner",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscription",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(maxLength: 100, nullable: false),
                    document_id = table.Column<int>(nullable: false),
                    type = table.Column<byte>(nullable: false),
                    partner_id = table.Column<int>(nullable: true),
                    temporary = table.Column<bool>(nullable: false),
                    validator_id = table.Column<string>(maxLength: 200, nullable: true),
                    handler_id = table.Column<string>(maxLength: 200, nullable: true),
                    mapper_id = table.Column<string>(maxLength: 200, nullable: true),
                    validator_properties = table.Column<IReadOnlyDictionary<string, string>>(type: "jsonb", nullable: true),
                    handler_properties = table.Column<IReadOnlyDictionary<string, string>>(type: "jsonb", nullable: true),
                    mapper_properties = table.Column<IReadOnlyDictionary<string, string>>(type: "jsonb", nullable: true),
                    receiver_properties = table.Column<IReadOnlyDictionary<string, string>>(type: "jsonb", nullable: true),
                    document_filter = table.Column<IReadOnlyDictionary<string, string>>(type: "jsonb", nullable: true),
                    inactive = table.Column<bool>(nullable: false),
                    response_subscription_id = table.Column<int>(nullable: true),
                    response_message_type_name = table.Column<string>(maxLength: 500, nullable: true),
                    aggregation_for_id = table.Column<int>(nullable: true),
                    aggregation_target = table.Column<byte>(nullable: false),
                    aggregate_on = table.Column<DateTime>(nullable: true),
                    receiver_id = table.Column<string>(maxLength: 200, nullable: true),
                    receive_on = table.Column<DateTime>(nullable: true),
                    consecutive_failures = table.Column<int>(nullable: false),
                    last_exception = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_aggregation_for",
                        column: x => x.aggregation_for_id,
                        principalSchema: "infolink",
                        principalTable: "subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_document_document_id",
                        column: x => x.document_id,
                        principalSchema: "infolink",
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_partner_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "infolink",
                        principalTable: "partner",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscription_response_subscriber",
                        column: x => x.response_subscription_id,
                        principalSchema: "infolink",
                        principalTable: "subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "xchange_aggregation",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<string>(maxLength: 50, nullable: false),
                    aggregated_on = table.Column<DateTime>(nullable: false),
                    aggregation_xchange_id = table.Column<string>(maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xchange_aggregation", x => x.id);
                    table.ForeignKey(
                        name: "fk_xchange_aggregation_xchange_xchange_id",
                        column: x => x.id,
                        principalSchema: "infolink",
                        principalTable: "xchange",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "xchange_delivery",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<string>(maxLength: 50, nullable: false),
                    delivered_on = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xchange_delivery", x => x.id);
                    table.ForeignKey(
                        name: "fk_xchange_delivery_xchange_xchange_id",
                        column: x => x.id,
                        principalSchema: "infolink",
                        principalTable: "xchange",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "xchange_promoted_properties",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<string>(maxLength: 50, nullable: false),
                    properties = table.Column<IReadOnlyDictionary<string, string>>(type: "jsonb", nullable: true),
                    hits = table.Column<int[]>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xchange_promoted_properties", x => x.id);
                    table.ForeignKey(
                        name: "fk_xchange_promoted_properties_xchange_xchange_id",
                        column: x => x.id,
                        principalSchema: "infolink",
                        principalTable: "xchange",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "xchange_result",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<string>(maxLength: 50, nullable: false),
                    success = table.Column<bool>(nullable: false),
                    exception = table.Column<string>(nullable: true),
                    finished_on = table.Column<DateTime>(nullable: false),
                    response_xchange_id = table.Column<string>(nullable: true),
                    output_name = table.Column<string>(maxLength: 200, nullable: true),
                    output_size = table.Column<int>(nullable: false),
                    output_hash = table.Column<string>(maxLength: 50, nullable: true),
                    output_bad = table.Column<bool>(nullable: false),
                    output_content_type = table.Column<string>(maxLength: 200, nullable: true),
                    response_name = table.Column<string>(maxLength: 200, nullable: true),
                    response_size = table.Column<int>(nullable: false),
                    response_hash = table.Column<string>(maxLength: 50, nullable: true),
                    response_bad = table.Column<bool>(nullable: false),
                    response_content_type = table.Column<string>(maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xchange_result", x => x.id);
                    table.ForeignKey(
                        name: "fk_xchange_result_xchange_xchange_id",
                        column: x => x.id,
                        principalSchema: "infolink",
                        principalTable: "xchange",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscription_schedule",
                schema: "infolink",
                columns: table => new
                {
                    subscription_id = table.Column<int>(nullable: false),
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    recurrence = table.Column<byte>(nullable: false),
                    on = table.Column<long>(nullable: false),
                    backwards = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule", x => new { x.subscription_id, x.id });
                    table.ForeignKey(
                        name: "fk_schedule_subscription_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "infolink",
                        principalTable: "subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "infolink",
                table: "document",
                columns: new[] { "id", "bus_enabled", "bus_message_type_name", "duplicate_interval", "name", "promoted_properties" },
                values: new object[] { 10001, false, null, 0, "Aggregation Document", "{}" });

            migrationBuilder.InsertData(
                schema: "infolink",
                table: "partner",
                columns: new[] { "id", "name" },
                values: new object[] { 1, "SYSTEM" });

            migrationBuilder.InsertData(
                schema: "infolink",
                table: "partner_api_credential",
                columns: new[] { "partner_id", "id", "key", "name" },
                values: new object[] { 1, 1, "7facc758283844b49cc4ffd26a75b1de", "default" });

            migrationBuilder.CreateIndex(
                name: "ix_document_bus_message_type_name",
                schema: "infolink",
                table: "document",
                column: "bus_message_type_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_name",
                schema: "infolink",
                table: "document",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_partner_api_credential_key",
                schema: "infolink",
                table: "partner_api_credential",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_aggregation_for_id",
                schema: "infolink",
                table: "subscription",
                column: "aggregation_for_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_document_id",
                schema: "infolink",
                table: "subscription",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_partner_id",
                schema: "infolink",
                table: "subscription",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_response_subscription_id",
                schema: "infolink",
                table: "subscription",
                column: "response_subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_xchange_document_id",
                schema: "infolink",
                table: "xchange",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_xchange_input_hash",
                schema: "infolink",
                table: "xchange",
                column: "input_hash");

            migrationBuilder.CreateIndex(
                name: "ix_xchange_retry_for",
                schema: "infolink",
                table: "xchange",
                column: "retry_for");

            migrationBuilder.CreateIndex(
                name: "ix_xchange_started_on",
                schema: "infolink",
                table: "xchange",
                column: "started_on");

            migrationBuilder.CreateIndex(
                name: "ix_xchange_subscription_id",
                schema: "infolink",
                table: "xchange",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_xchange_aggregation_aggregation_xchange_id",
                schema: "infolink",
                table: "xchange_aggregation",
                column: "aggregation_xchange_id");

            migrationBuilder.CreateIndex(
                name: "ix_xchange_delivery_delivered_on",
                schema: "infolink",
                table: "xchange_delivery",
                column: "delivered_on");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partner_api_credential",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "subscription_schedule",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "xchange_aggregation",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "xchange_delivery",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "xchange_promoted_properties",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "xchange_result",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "subscription",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "xchange",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "partner",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "document",
                schema: "infolink");
        }
    }
}
