using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class PartnerAndGlobalSecretProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "secret_properties",
                schema: "infolink",
                table: "partner",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "secret_properties",
                schema: "infolink",
                table: "global_adapter_values_set",
                type: "jsonb",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "partner",
                keyColumn: "id",
                keyValue: 1,
                column: "secret_properties",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "secret_properties",
                schema: "infolink",
                table: "partner");

            migrationBuilder.DropColumn(
                name: "secret_properties",
                schema: "infolink",
                table: "global_adapter_values_set");
        }
    }
}
