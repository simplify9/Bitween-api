using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
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
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Status",
                table: "Xchanges",
                type: "tinyint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "XchangeStateFill",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    FillBegan = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PositionStartedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PositionId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeStateFill", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Built by the status fill, not by a migration, and SQL Server won't drop a column an
            // index uses.
            migrationBuilder.Sql("DROP INDEX IF EXISTS [ix_xchange_state] ON [Xchanges];");

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
