using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class PartnerOwnedRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_roles_name",
                schema: "infolink",
                table: "Roles");

            migrationBuilder.AddColumn<int>(
                name: "partner_id",
                schema: "infolink",
                table: "Roles",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 1,
                column: "partner_id",
                value: null);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 2,
                column: "partner_id",
                value: null);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 3,
                column: "partner_id",
                value: null);

            migrationBuilder.CreateIndex(
                name: "ix_roles_partner_id_name",
                schema: "infolink",
                table: "Roles",
                columns: new[] { "partner_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_roles_partner_id_name",
                schema: "infolink",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "partner_id",
                schema: "infolink",
                table: "Roles");

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                schema: "infolink",
                table: "Roles",
                column: "name",
                unique: true);
        }
    }
}
