using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update7 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifiers",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    RunOnSuccessfulResult = table.Column<bool>(nullable: false),
                    RunOnBadResult = table.Column<bool>(nullable: false),
                    RunOnFailedResult = table.Column<bool>(nullable: false),
                    HandlerId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    Inactive = table.Column<bool>(nullable: false),
                    HandlerProperties = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifiers", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifiers");
        }
    }
}
