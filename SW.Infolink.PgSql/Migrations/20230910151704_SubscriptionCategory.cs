using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Infolink.PgSql.Migrations
{
    public partial class SubscriptionCategory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "category_id",
                schema: "infolink",
                table: "subscription",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subscription_category",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    modified_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_category", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_category_id",
                schema: "infolink",
                table: "subscription",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_category_code",
                schema: "infolink",
                table: "subscription_category",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_subscription_subscription_category_category_id",
                schema: "infolink",
                table: "subscription",
                column: "category_id",
                principalSchema: "infolink",
                principalTable: "subscription_category",
                principalColumn: "id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_subscription_subscription_category_category_id",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropTable(
                name: "subscription_category",
                schema: "infolink");

            migrationBuilder.DropIndex(
                name: "ix_subscription_category_id",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "infolink",
                table: "subscription");
        }
    }
}
