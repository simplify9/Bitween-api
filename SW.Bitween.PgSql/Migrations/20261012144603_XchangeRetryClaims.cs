using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class XchangeRetryClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "xchange_retry_claim",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    retry_xchange_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    claimed_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xchange_retry_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_xchange_retry_claim_xchange_id",
                        column: x => x.id,
                        principalSchema: "infolink",
                        principalTable: "xchange",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "xchange_retry_claim",
                schema: "infolink");
        }
    }
}
