using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionAdapterVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "handler_version",
                schema: "infolink",
                table: "subscription",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mapper_version",
                schema: "infolink",
                table: "subscription",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receiver_version",
                schema: "infolink",
                table: "subscription",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "validator_version",
                schema: "infolink",
                table: "subscription",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "handler_version",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropColumn(
                name: "mapper_version",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropColumn(
                name: "receiver_version",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropColumn(
                name: "validator_version",
                schema: "infolink",
                table: "subscription");
        }
    }
}
