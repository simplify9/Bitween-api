using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Infolink.PgSql.Migrations
{
    public partial class update13 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "infolink",
                table: "Accounts",
                columns: new[] { "id", "created_by", "created_on", "deleted", "disabled", "display_name", "email", "email_provider", "login_methods", "modified_by", "modified_on", "password", "phone" },
                values: new object[] { 9999, null, new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc), false, false, "Admin", "admin@infolink.systems", (byte)0, (byte)2, null, null, "$SWHASH$V1$10000$VQCi48eitH4Ml5juvBMOFZrMdQwBbhuIQVXe6RR7qJdDF2bJ", null });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "infolink",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999);
        }
    }
}
