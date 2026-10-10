using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class OperatorActionsAndLastSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSignInOn",
                table: "Accounts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperatorActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AccountId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OccurredOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorActions", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 9999,
                column: "LastSignInOn",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_OperatorActions_OccurredOn",
                table: "OperatorActions",
                column: "OccurredOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperatorActions");

            migrationBuilder.DropColumn(
                name: "LastSignInOn",
                table: "Accounts");
        }
    }
}
