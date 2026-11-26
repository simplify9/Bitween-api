using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class PartnerPortalAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ForPartners",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PresetKey",
                table: "Roles",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartnerId",
                table: "Accounts",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 9999,
                column: "PartnerId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ForPartners", "PresetKey" },
                values: new object[] { false, null });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "ForPartners", "PresetKey" },
                values: new object[] { false, null });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "ForPartners", "PresetKey" },
                values: new object[] { false, null });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_PresetKey",
                table: "Roles",
                column: "PresetKey",
                unique: true,
                filter: "[PresetKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_PartnerId",
                table: "Accounts",
                column: "PartnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roles_PresetKey",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_PartnerId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ForPartners",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "PresetKey",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "PartnerId",
                table: "Accounts");
        }
    }
}
