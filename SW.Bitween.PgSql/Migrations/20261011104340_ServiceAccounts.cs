using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class ServiceAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "kind",
                schema: "infolink",
                table: "Accounts",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "ServiceTokens",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    shown = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    expires_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_used_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_used_from = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: true),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    modified_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_tokens_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "infolink",
                        principalTable: "Accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999,
                column: "kind",
                value: (byte)0);

            migrationBuilder.CreateIndex(
                name: "ix_service_tokens_account_id",
                schema: "infolink",
                table: "ServiceTokens",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_tokens_hash",
                schema: "infolink",
                table: "ServiceTokens",
                column: "hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceTokens",
                schema: "infolink");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "infolink",
                table: "Accounts");
        }
    }
}
