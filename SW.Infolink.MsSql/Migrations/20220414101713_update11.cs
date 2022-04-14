using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Infolink.MsSql.Migrations
{
    public partial class update11 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmailProvider = table.Column<byte>(type: "tinyint", nullable: false),
                    LoginMethods = table.Column<byte>(type: "tinyint", nullable: false),
                    Disabled = table.Column<bool>(type: "bit", nullable: false),
                    Password = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    LoginMethod = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "Id", "CreatedBy", "CreatedOn", "Disabled", "DisplayName", "Email", "EmailProvider", "LoginMethods", "ModifiedBy", "ModifiedOn", "Password", "Phone" },
                values: new object[] { 9999, null, new DateTime(2022, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), false, "Admin", "admin@infolink.systems", (byte)0, (byte)2, null, null, "$SWHASH$V1$10000$VQCi48eitH4Ml5juvBMOFZrMdQwBbhuIQVXe6RR7qJdDF2bJ", null });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Email",
                table: "Accounts",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_AccountId",
                table: "RefreshTokens",
                column: "AccountId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
