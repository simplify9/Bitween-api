using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class OperatorActionsAndLastSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_sign_in_on",
                schema: "infolink",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "operator_action",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    account_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    occurred_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_action", x => x.id);
                });

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999,
                column: "last_sign_in_on",
                value: null);

            migrationBuilder.CreateIndex(
                name: "ix_operator_action_occurred_on",
                schema: "infolink",
                table: "operator_action",
                column: "occurred_on");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operator_action",
                schema: "infolink");

            migrationBuilder.DropColumn(
                name: "last_sign_in_on",
                schema: "infolink",
                table: "Accounts");
        }
    }
}
