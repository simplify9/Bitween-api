using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "XchangePromotedProperties",
                columns: table => new
                {
                    Id = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
                    XchangeId = table.Column<string>(unicode: false, maxLength: 50, nullable: true),
                    Value = table.Column<string>(nullable: true),
                    Properties = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangePromotedProperties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XchangePromotedProperties_Xchanges_XchangeId",
                        column: x => x.XchangeId,
                        principalTable: "Xchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_XchangePromotedProperties_XchangeId",
                table: "XchangePromotedProperties",
                column: "XchangeId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XchangePromotedProperties");
        }
    }
}
