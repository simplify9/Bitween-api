using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class XchangeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Retried",
                table: "Xchanges",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Status",
                table: "Xchanges",
                type: "tinyint unsigned",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "XchangeStateFill",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    FillBegan = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PositionStartedOn = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PositionId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedOn = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeStateFill", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XchangeStateFill");

            migrationBuilder.DropColumn(
                name: "Retried",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Xchanges");
        }
    }
}
