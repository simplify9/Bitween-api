using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AccountLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountLinks",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    purpose = table.Column<byte>(type: "smallint", nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    expires_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    emailed_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    email_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_links_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "infolink",
                        principalTable: "Accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_links_account_id",
                schema: "infolink",
                table: "AccountLinks",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_links_hash",
                schema: "infolink",
                table: "AccountLinks",
                column: "hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountLinks",
                schema: "infolink");
        }
    }
}
