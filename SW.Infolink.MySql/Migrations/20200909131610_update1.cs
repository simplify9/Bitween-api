using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Aggregate",
                table: "Subscribers");

            migrationBuilder.AddColumn<int>(
                name: "AggregationForId",
                table: "Subscribers",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "XchangeAggregations",
                columns: table => new
                {
                    Id = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
                    AggregatedOn = table.Column<DateTime>(nullable: false),
                    AggregationXchangeId = table.Column<string>(unicode: false, maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeAggregations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XchangeAggregations_Xchanges_Id",
                        column: x => x.Id,
                        principalTable: "Xchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Documents",
                columns: new[] { "Id", "BusEnabled", "BusMessageTypeName", "DuplicateInterval", "Name", "PromotedProperties" },
                values: new object[] { 10001, false, null, 0, "Aggregation Document", "{}" });

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_StartedOn",
                table: "Xchanges",
                column: "StartedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Subscribers_AggregationForId",
                table: "Subscribers",
                column: "AggregationForId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XchangeAggregations_AggregationXchangeId",
                table: "XchangeAggregations",
                column: "AggregationXchangeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subscribers_Subscribers_AggregationForId",
                table: "Subscribers",
                column: "AggregationForId",
                principalTable: "Subscribers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subscribers_Subscribers_AggregationForId",
                table: "Subscribers");

            migrationBuilder.DropTable(
                name: "XchangeAggregations");

            migrationBuilder.DropIndex(
                name: "IX_Xchanges_StartedOn",
                table: "Xchanges");

            migrationBuilder.DropIndex(
                name: "IX_Subscribers_AggregationForId",
                table: "Subscribers");

            migrationBuilder.DeleteData(
                table: "Documents",
                keyColumn: "Id",
                keyValue: 10001);

            migrationBuilder.DropColumn(
                name: "AggregationForId",
                table: "Subscribers");

            migrationBuilder.AddColumn<bool>(
                name: "Aggregate",
                table: "Subscribers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
