using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "channel_id",
                schema: "infolink",
                table: "xchange_notification",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notifications",
                schema: "infolink",
                table: "subscription",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "alert_channel_id",
                schema: "infolink",
                table: "retry_policy",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "alert_channel_id",
                schema: "infolink",
                table: "retry_alert_override",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notification_channel",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    handler_id = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: false),
                    handler_properties = table.Column<string>(type: "text", nullable: true),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    modified_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_channel", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_xchange_notification_channel_id_finished_on",
                schema: "infolink",
                table: "xchange_notification",
                columns: new[] { "channel_id", "finished_on" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_channel_name",
                schema: "infolink",
                table: "notification_channel",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_channel",
                schema: "infolink");

            migrationBuilder.DropIndex(
                name: "ix_xchange_notification_channel_id_finished_on",
                schema: "infolink",
                table: "xchange_notification");

            migrationBuilder.DropColumn(
                name: "channel_id",
                schema: "infolink",
                table: "xchange_notification");

            migrationBuilder.DropColumn(
                name: "notifications",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropColumn(
                name: "alert_channel_id",
                schema: "infolink",
                table: "retry_policy");

            migrationBuilder.DropColumn(
                name: "alert_channel_id",
                schema: "infolink",
                table: "retry_alert_override");
        }
    }
}
