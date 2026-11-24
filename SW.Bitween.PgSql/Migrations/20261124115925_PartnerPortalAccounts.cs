using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class PartnerPortalAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "for_partners",
                schema: "infolink",
                table: "Roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "preset_key",
                schema: "infolink",
                table: "Roles",
                type: "character varying(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "partner_id",
                schema: "infolink",
                table: "Accounts",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999,
                column: "partner_id",
                value: null);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 1,
                columns: new[] { "for_partners", "preset_key" },
                values: new object[] { false, null });

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 2,
                columns: new[] { "for_partners", "preset_key" },
                values: new object[] { false, null });

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Roles",
                keyColumn: "id",
                keyValue: 3,
                columns: new[] { "for_partners", "preset_key" },
                values: new object[] { false, null });

            migrationBuilder.CreateIndex(
                name: "ix_roles_preset_key",
                schema: "infolink",
                table: "Roles",
                column: "preset_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_partner_id",
                schema: "infolink",
                table: "Accounts",
                column: "partner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_roles_preset_key",
                schema: "infolink",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "ix_accounts_partner_id",
                schema: "infolink",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "for_partners",
                schema: "infolink",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "preset_key",
                schema: "infolink",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "partner_id",
                schema: "infolink",
                table: "Accounts");
        }
    }
}
