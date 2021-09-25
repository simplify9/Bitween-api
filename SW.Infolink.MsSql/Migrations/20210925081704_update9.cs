using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MsSql.Migrations
{
    public partial class update9 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RunOnSubscriptions",
                table: "Notifiers",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RunOnSubscriptions",
                table: "Notifiers");
        }
    }
}
