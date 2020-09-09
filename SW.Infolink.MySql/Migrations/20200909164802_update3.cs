using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_DocumentId",
                table: "Xchanges",
                column: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Xchanges_Documents_DocumentId",
                table: "Xchanges",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Xchanges_Documents_DocumentId",
                table: "Xchanges");

            migrationBuilder.DropIndex(
                name: "IX_Xchanges_DocumentId",
                table: "Xchanges");
        }
    }
}
