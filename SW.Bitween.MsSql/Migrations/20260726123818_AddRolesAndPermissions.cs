using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Permissions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountRoles",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountRoles", x => new { x.AccountId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AccountRoles_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "CreatedBy", "CreatedOn", "Description", "IsSystem", "ModifiedBy", "ModifiedOn", "Name", "Permissions" },
                values: new object[,]
                {
                    { 1, null, new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc), "Full access to everything, including members, roles and settings.", true, null, null, "Administrator", "[]" },
                    { 2, null, new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc), "Runs and configures integrations. Can't manage members, roles or settings.", true, null, null, "Member", "[]" },
                    { 3, null, new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc), "Read-only access to integrations, exchanges and configuration.", true, null, null, "Viewer", "[]" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountRoles_RoleId",
                table: "AccountRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            // Every existing account keeps exactly the access it had: its coarse AccountRole
            // (Admin=0, Viewer=10, Member=20) becomes the matching built-in role. Anything
            // unrecognised lands on Viewer — least privilege. Idempotent, so re-running or
            // ordering against the seeded admin account can't produce duplicates.
            migrationBuilder.Sql("""
                INSERT INTO [AccountRoles] ([AccountId], [RoleId])
                SELECT a.[Id], CASE a.[Role] WHEN 0 THEN 1 WHEN 20 THEN 2 ELSE 3 END
                FROM [Accounts] a
                WHERE NOT EXISTS (
                    SELECT 1 FROM [AccountRoles] l WHERE l.[AccountId] = a.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountRoles");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
