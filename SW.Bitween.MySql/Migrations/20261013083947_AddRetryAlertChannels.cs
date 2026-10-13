using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryAlertChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AlertChannelId",
                table: "RetryPolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AlertChannelId",
                table: "RetryAlertOverrides",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlertChannelId",
                table: "RetryPolicies");

            migrationBuilder.DropColumn(
                name: "AlertChannelId",
                table: "RetryAlertOverrides");
        }
    }
}
