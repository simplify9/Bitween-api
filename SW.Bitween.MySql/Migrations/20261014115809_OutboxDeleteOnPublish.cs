using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <summary>
    /// Published outbox rows are deleted now rather than marked and kept a day so the dispatcher's index leads with the
    /// published date, and each node reports what it published on its heartbeat.
    /// XchangeDeliveries goes: it was never written. Secondary indexes build online on InnoDB.
    /// </summary>
    public partial class OutboxDeleteOnPublish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XchangeDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_PublishedOn",
                table: "OutboxMessages");

            migrationBuilder.AddColumn<int>(
                name: "OutboxPublishedLastHour",
                table: "ClusterNodes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedOn_CreatedOn",
                table: "OutboxMessages",
                columns: new[] { "PublishedOn", "CreatedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_PublishedOn_CreatedOn",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "OutboxPublishedLastHour",
                table: "ClusterNodes");

            migrationBuilder.CreateTable(
                name: "XchangeDeliveries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveredOn = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XchangeDeliveries_Xchanges_Id",
                        column: x => x.Id,
                        principalTable: "Xchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedOn",
                table: "OutboxMessages",
                column: "PublishedOn");

            migrationBuilder.CreateIndex(
                name: "IX_XchangeDeliveries_DeliveredOn",
                table: "XchangeDeliveries",
                column: "DeliveredOn");
        }
    }
}
