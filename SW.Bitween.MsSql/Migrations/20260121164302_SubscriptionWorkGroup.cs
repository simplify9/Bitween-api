using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionWorkGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkGroupId",
                table: "Subscriptions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkGroup",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BusMessageName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Options = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkGroup", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_WorkGroupId",
                table: "Subscriptions",
                column: "WorkGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_WorkGroup_WorkGroupId",
                table: "Subscriptions",
                column: "WorkGroupId",
                principalTable: "WorkGroup",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_WorkGroup_WorkGroupId",
                table: "Subscriptions");

            migrationBuilder.DropTable(
                name: "WorkGroup");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_WorkGroupId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(    
                name: "WorkGroupId",
                table: "Subscriptions");
        }
    }
}
