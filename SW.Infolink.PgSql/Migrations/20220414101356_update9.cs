using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Infolink.PgSql.Migrations
{
    public partial class update9 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_api_credential_partner_partner_id",
                schema: "infolink",
                table: "partner_api_credential");

            migrationBuilder.DropForeignKey(
                name: "fk_schedule_subscription_subscription_id",
                schema: "infolink",
                table: "subscription_schedule");

            migrationBuilder.DropPrimaryKey(
                name: "pk_schedule",
                schema: "infolink",
                table: "subscription_schedule");

            migrationBuilder.DropPrimaryKey(
                name: "pk_api_credential",
                schema: "infolink",
                table: "partner_api_credential");

            migrationBuilder.AlterColumn<DateTime>(
                name: "finished_on",
                schema: "infolink",
                table: "xchange_result",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "finished_on",
                schema: "infolink",
                table: "xchange_notification",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "delivered_on",
                schema: "infolink",
                table: "xchange_delivery",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "aggregated_on",
                schema: "infolink",
                table: "xchange_aggregation",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "started_on",
                schema: "infolink",
                table: "xchange",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "receive_on",
                schema: "infolink",
                table: "subscription",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "paused_on",
                schema: "infolink",
                table: "subscription",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "aggregate_on",
                schema: "infolink",
                table: "subscription",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_subscription_schedule",
                schema: "infolink",
                table: "subscription_schedule",
                columns: new[] { "subscription_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_partner_api_credential",
                schema: "infolink",
                table: "partner_api_credential",
                columns: new[] { "partner_id", "id" });

            migrationBuilder.CreateTable(
                name: "Accounts",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email_provider = table.Column<byte>(type: "smallint", nullable: false),
                    login_methods = table.Column<byte>(type: "smallint", nullable: false),
                    disabled = table.Column<bool>(type: "boolean", nullable: false),
                    password = table.Column<string>(type: "character varying(500)", unicode: false, maxLength: 500, nullable: true),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    modified_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "running_result",
                schema: "infolink",
                columns: table => new
                {
                    is_running = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: false),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    login_method = table.Column<byte>(type: "smallint", nullable: false),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "infolink",
                        principalTable: "Accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "infolink",
                table: "Accounts",
                columns: new[] { "id", "created_by", "created_on", "disabled", "display_name", "email", "email_provider", "login_methods", "modified_by", "modified_on", "password", "phone" },
                values: new object[] { 9999, null, new DateTime(2022, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, "Admin", "admin@infolink.systems", (byte)0, (byte)2, null, null, "$SWHASH$V1$10000$VQCi48eitH4Ml5juvBMOFZrMdQwBbhuIQVXe6RR7qJdDF2bJ", null });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_email",
                schema: "infolink",
                table: "Accounts",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_account_id",
                schema: "infolink",
                table: "RefreshTokens",
                column: "account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_partner_api_credential_partner_partner_id",
                schema: "infolink",
                table: "partner_api_credential",
                column: "partner_id",
                principalSchema: "infolink",
                principalTable: "partner",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_subscription_schedule_subscription_subscription_id",
                schema: "infolink",
                table: "subscription_schedule",
                column: "subscription_id",
                principalSchema: "infolink",
                principalTable: "subscription",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_partner_api_credential_partner_partner_id",
                schema: "infolink",
                table: "partner_api_credential");

            migrationBuilder.DropForeignKey(
                name: "fk_subscription_schedule_subscription_subscription_id",
                schema: "infolink",
                table: "subscription_schedule");

            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "running_result",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "Accounts",
                schema: "infolink");

            migrationBuilder.DropPrimaryKey(
                name: "pk_subscription_schedule",
                schema: "infolink",
                table: "subscription_schedule");

            migrationBuilder.DropPrimaryKey(
                name: "pk_partner_api_credential",
                schema: "infolink",
                table: "partner_api_credential");

            migrationBuilder.AlterColumn<DateTime>(
                name: "finished_on",
                schema: "infolink",
                table: "xchange_result",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "finished_on",
                schema: "infolink",
                table: "xchange_notification",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "delivered_on",
                schema: "infolink",
                table: "xchange_delivery",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "aggregated_on",
                schema: "infolink",
                table: "xchange_aggregation",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "started_on",
                schema: "infolink",
                table: "xchange",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "receive_on",
                schema: "infolink",
                table: "subscription",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "paused_on",
                schema: "infolink",
                table: "subscription",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "aggregate_on",
                schema: "infolink",
                table: "subscription",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_schedule",
                schema: "infolink",
                table: "subscription_schedule",
                columns: new[] { "subscription_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_api_credential",
                schema: "infolink",
                table: "partner_api_credential",
                columns: new[] { "partner_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_api_credential_partner_partner_id",
                schema: "infolink",
                table: "partner_api_credential",
                column: "partner_id",
                principalSchema: "infolink",
                principalTable: "partner",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_schedule_subscription_subscription_id",
                schema: "infolink",
                table: "subscription_schedule",
                column: "subscription_id",
                principalSchema: "infolink",
                principalTable: "subscription",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
