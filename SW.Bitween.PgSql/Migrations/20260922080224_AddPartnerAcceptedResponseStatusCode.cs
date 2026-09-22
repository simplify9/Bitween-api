using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerAcceptedResponseStatusCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "accepted_response_status_code",
                schema: "infolink",
                table: "partner",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "partner",
                keyColumn: "id",
                keyValue: 1,
                column: "accepted_response_status_code",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accepted_response_status_code",
                schema: "infolink",
                table: "partner");
        }
    }
}
