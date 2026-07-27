using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    permissions = table.Column<string>(type: "text", nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    modified_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "AccountRoles",
                schema: "infolink",
                columns: table => new
                {
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    role_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_roles", x => new { x.account_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_account_roles_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "infolink",
                        principalTable: "Accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_account_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "infolink",
                        principalTable: "Roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "infolink",
                table: "Roles",
                columns: new[] { "id", "created_by", "created_on", "description", "is_system", "modified_by", "modified_on", "name", "permissions" },
                values: new object[,]
                {
                    { 1, null, new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc), "Full access to everything, including members, roles and settings.", true, null, null, "Administrator", "[]" },
                    { 2, null, new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc), "Runs and configures integrations. Can't manage members, roles or settings.", true, null, null, "Member", "[]" },
                    { 3, null, new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc), "Read-only access to integrations, exchanges and configuration.", true, null, null, "Viewer", "[]" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_roles_role_id",
                schema: "infolink",
                table: "AccountRoles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                schema: "infolink",
                table: "Roles",
                column: "name",
                unique: true);

            // Every existing account keeps exactly the access it had: its coarse AccountRole
            // (Admin=0, Viewer=10, Member=20) becomes the matching built-in role. Anything
            // unrecognised lands on Viewer — least privilege. Idempotent, so re-running or
            // ordering against the seeded admin account can't produce duplicates.
            migrationBuilder.Sql("""
                INSERT INTO infolink."AccountRoles" (account_id, role_id)
                SELECT a.id, CASE a."role" WHEN 0 THEN 1 WHEN 20 THEN 2 ELSE 3 END
                FROM infolink."Accounts" a
                WHERE NOT EXISTS (
                    SELECT 1 FROM infolink."AccountRoles" l WHERE l.account_id = a.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountRoles",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "infolink");
        }
    }
}
