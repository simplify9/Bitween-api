using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    public partial class update12 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "Bitween",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999);

            migrationBuilder.AddColumn<bool>(
                name: "deleted",
                schema: "Bitween",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted",
                schema: "Bitween",
                table: "Accounts");

            migrationBuilder.InsertData(
                schema: "Bitween",
                table: "Accounts",
                columns: new[] { "id", "created_by", "created_on", "disabled", "display_name", "email", "email_provider", "login_methods", "modified_by", "modified_on", "password", "phone" },
                values: new object[] { 9999, null, new DateTime(2022, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), false, "Admin", "admin@Bitween.systems", (byte)0, (byte)2, null, null, "$SWHASH$V1$10000$VQCi48eitH4Ml5juvBMOFZrMdQwBbhuIQVXe6RR7qJdDF2bJ", null });
        }
    }
}
