using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerKeyPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "key_prefix",
                schema: "infolink",
                table: "partner_api_credential",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "partner_api_credential",
                keyColumns: new[] { "id", "partner_id" },
                keyValues: new object[] { 1, 1 },
                column: "key_prefix",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "key_prefix",
                schema: "infolink",
                table: "partner_api_credential");
        }
    }
}
