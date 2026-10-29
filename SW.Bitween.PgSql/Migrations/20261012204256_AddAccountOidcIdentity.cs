using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountOidcIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "oidc_identity",
                schema: "infolink",
                table: "Accounts",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999,
                column: "oidc_identity",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "oidc_identity",
                schema: "infolink",
                table: "Accounts");
        }
    }
}
