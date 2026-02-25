using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class ApiGateWayAndGlobalValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdapterProperties",
                table: "Partners",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApiGateways",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UrlName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiGateways", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalAdapterValuesSets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Values = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalAdapterValuesSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiGatewayPartners",
                columns: table => new
                {
                    ApiGatewayId = table.Column<int>(type: "int", nullable: false),
                    PartnerId = table.Column<int>(type: "int", nullable: false),
                    SubscriptionId = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiGatewayPartners", x => new { x.ApiGatewayId, x.PartnerId, x.SubscriptionId });
                    table.ForeignKey(
                        name: "FK_ApiGatewayPartners_ApiGateways_ApiGatewayId",
                        column: x => x.ApiGatewayId,
                        principalTable: "ApiGateways",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApiGatewayPartners_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApiGatewayPartners_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            
            migrationBuilder.CreateIndex(
                name: "IX_ApiGatewayPartners_PartnerId",
                table: "ApiGatewayPartners",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiGatewayPartners_SubscriptionId",
                table: "ApiGatewayPartners",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiGateways_UrlName",
                table: "ApiGateways",
                column: "UrlName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiGatewayPartners");

            migrationBuilder.DropTable(
                name: "GlobalAdapterValuesSets");

            migrationBuilder.DropTable(
                name: "ApiGateways");

            migrationBuilder.DropColumn(
                name: "AdapterProperties",
                table: "Partners");
        }
    }
}
