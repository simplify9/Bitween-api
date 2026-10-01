using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class ExchangeRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilesPrefix",
                table: "Xchanges",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_XchangeNotifications_XchangeId",
                table: "XchangeNotifications",
                column: "XchangeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_XchangeNotifications_XchangeId",
                table: "XchangeNotifications");

            migrationBuilder.DropColumn(
                name: "FilesPrefix",
                table: "Xchanges");
        }
    }
}
