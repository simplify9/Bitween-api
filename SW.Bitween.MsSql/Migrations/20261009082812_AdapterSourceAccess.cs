using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class AdapterSourceAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdapterSourceAccesses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdapterId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Version = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AccountId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    OccurredOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdapterSourceAccesses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdapterSourceAccesses_AdapterId_OccurredOn",
                table: "AdapterSourceAccesses",
                columns: new[] { "AdapterId", "OccurredOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdapterSourceAccesses");
        }
    }
}
