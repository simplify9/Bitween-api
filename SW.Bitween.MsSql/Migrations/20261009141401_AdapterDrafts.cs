using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class AdapterDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdapterDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdapterId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Language = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    BaseVersion = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    FilesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilesHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdapterDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdapterReleases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdapterId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Version = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    DraftId = table.Column<int>(type: "int", nullable: true),
                    AccountId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    OccurredOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdapterReleases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdapterDrafts_AdapterId",
                table: "AdapterDrafts",
                column: "AdapterId");

            migrationBuilder.CreateIndex(
                name: "IX_AdapterReleases_AdapterId_OccurredOn",
                table: "AdapterReleases",
                columns: new[] { "AdapterId", "OccurredOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdapterDrafts");

            migrationBuilder.DropTable(
                name: "AdapterReleases");
        }
    }
}
