using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class ClusterNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClusterNodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Host = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    StartedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    DataSources = table.Column<bool>(type: "bit", nullable: false),
                    Runtimes = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClusterNodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClusterNodes_LastSeenOn",
                table: "ClusterNodes",
                column: "LastSeenOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClusterNodes");
        }
    }
}
