using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class AddBackgroundRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackgroundRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Page = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Node = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Done = table.Column<int>(type: "int", nullable: true),
                    Total = table.Column<int>(type: "int", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResultStatus = table.Column<int>(type: "int", nullable: true),
                    ResultContentType = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    ResultDisposition = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Result = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ResultKey = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundRequests_Owner_StartedOn",
                table: "BackgroundRequests",
                columns: new[] { "Owner", "StartedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundRequests_StartedOn",
                table: "BackgroundRequests",
                column: "StartedOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackgroundRequests");
        }
    }
}
