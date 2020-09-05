using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update4 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Subscribers_DocumentId",
                table: "Subscribers",
                column: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subscribers_Documents_DocumentId",
                table: "Subscribers",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subscribers_Documents_DocumentId",
                table: "Subscribers");

            migrationBuilder.DropIndex(
                name: "IX_Subscribers_DocumentId",
                table: "Subscribers");
        }
    }
}
