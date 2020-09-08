using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update9 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_XchangeDeliveries_Xchanges_XchangeId",
                table: "XchangeDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_XchangePromotedProperties_Xchanges_XchangeId",
                table: "XchangePromotedProperties");

            migrationBuilder.DropForeignKey(
                name: "FK_XchangeResults_Xchanges_XchangeId",
                table: "XchangeResults");

            migrationBuilder.DropIndex(
                name: "IX_XchangeResults_XchangeId",
                table: "XchangeResults");

            migrationBuilder.DropIndex(
                name: "IX_XchangePromotedProperties_XchangeId",
                table: "XchangePromotedProperties");

            migrationBuilder.DropIndex(
                name: "IX_XchangeDeliveries_XchangeId",
                table: "XchangeDeliveries");

            migrationBuilder.DropColumn(
                name: "XchangeId",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "XchangeId",
                table: "XchangePromotedProperties");

            migrationBuilder.DropColumn(
                name: "XchangeId",
                table: "XchangeDeliveries");

            migrationBuilder.AddForeignKey(
                name: "FK_XchangeDeliveries_Xchanges_Id",
                table: "XchangeDeliveries",
                column: "Id",
                principalTable: "Xchanges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_XchangePromotedProperties_Xchanges_Id",
                table: "XchangePromotedProperties",
                column: "Id",
                principalTable: "Xchanges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_XchangeResults_Xchanges_Id",
                table: "XchangeResults",
                column: "Id",
                principalTable: "Xchanges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_XchangeDeliveries_Xchanges_Id",
                table: "XchangeDeliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_XchangePromotedProperties_Xchanges_Id",
                table: "XchangePromotedProperties");

            migrationBuilder.DropForeignKey(
                name: "FK_XchangeResults_Xchanges_Id",
                table: "XchangeResults");

            migrationBuilder.AddColumn<string>(
                name: "XchangeId",
                table: "XchangeResults",
                type: "varchar(50) CHARACTER SET utf8mb4",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "XchangeId",
                table: "XchangePromotedProperties",
                type: "varchar(50) CHARACTER SET utf8mb4",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "XchangeId",
                table: "XchangeDeliveries",
                type: "varchar(50) CHARACTER SET utf8mb4",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_XchangeResults_XchangeId",
                table: "XchangeResults",
                column: "XchangeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XchangePromotedProperties_XchangeId",
                table: "XchangePromotedProperties",
                column: "XchangeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XchangeDeliveries_XchangeId",
                table: "XchangeDeliveries",
                column: "XchangeId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_XchangeDeliveries_Xchanges_XchangeId",
                table: "XchangeDeliveries",
                column: "XchangeId",
                principalTable: "Xchanges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_XchangePromotedProperties_Xchanges_XchangeId",
                table: "XchangePromotedProperties",
                column: "XchangeId",
                principalTable: "Xchanges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_XchangeResults_Xchanges_XchangeId",
                table: "XchangeResults",
                column: "XchangeId",
                principalTable: "Xchanges",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
