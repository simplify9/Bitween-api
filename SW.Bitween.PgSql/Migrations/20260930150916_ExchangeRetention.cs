using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class ExchangeRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "files_prefix",
                schema: "infolink",
                table: "xchange",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_xchange_notification_xchange_id",
                schema: "infolink",
                table: "xchange_notification",
                column: "xchange_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_xchange_notification_xchange_id",
                schema: "infolink",
                table: "xchange_notification");

            migrationBuilder.DropColumn(
                name: "files_prefix",
                schema: "infolink",
                table: "xchange");
        }
    }
}
