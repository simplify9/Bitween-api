using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SW.Bitween.Model;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionWorkGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
           
            migrationBuilder.AddColumn<int>(
                name: "work_group_id",
                schema: "infolink",
                table: "subscription",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "work_group",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: true),
                    bus_message_name = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: false),
                    options = table.Column<WorkGroupOptions>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_group", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_work_group_id",
                schema: "infolink",
                table: "subscription",
                column: "work_group_id");

            migrationBuilder.AddForeignKey(
                name: "fk_subscription_work_group_work_group_id",
                schema: "infolink",
                table: "subscription",
                column: "work_group_id",
                principalSchema: "infolink",
                principalTable: "work_group",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_subscription_work_group_work_group_id",
                schema: "infolink",
                table: "subscription");
            
            migrationBuilder.DropTable(
                name: "work_group",
                schema: "infolink");

            migrationBuilder.DropIndex(
                name: "ix_subscription_work_group_id",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropColumn(
                name: "work_group_id",
                schema: "infolink",
                table: "subscription");
            
        }
    }
}
