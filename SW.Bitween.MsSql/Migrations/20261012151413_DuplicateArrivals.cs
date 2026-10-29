using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class DuplicateArrivals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DropDuplicates",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DuplicateArrivals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    PartnerId = table.Column<int>(type: "int", nullable: true),
                    OriginalXchangeId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Dropped = table.Column<bool>(type: "bit", nullable: false),
                    OccurredOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuplicateArrivals", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Documents",
                keyColumn: "Id",
                keyValue: 10001,
                column: "DropDuplicates",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateArrivals_DocumentId_OccurredOn",
                table: "DuplicateArrivals",
                columns: new[] { "DocumentId", "OccurredOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DuplicateArrivals");

            migrationBuilder.DropColumn(
                name: "DropDuplicates",
                table: "Documents");
        }
    }
}
