using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class Update5 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionReceiveSchedules_Subscriptions_SubscriptionId",
                table: "SubscriptionReceiveSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_Subscriptions_AggregationForId",
                table: "Subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_Subscriptions_ResponseSubscriptionId",
                table: "Subscriptions");

            migrationBuilder.DropTable(
                name: "SubscriptionAggregationSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_AggregationForId",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_ResponseSubscriptionId",
                table: "Subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubscriptionReceiveSchedules",
                table: "SubscriptionReceiveSchedules");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionReceiveSchedules_SubscriptionId",
                table: "SubscriptionReceiveSchedules");

            migrationBuilder.RenameTable(
                name: "SubscriptionReceiveSchedules",
                newName: "SubscriptionSchedules");

            migrationBuilder.AlterColumn<long>(
                name: "On",
                table: "SubscriptionSchedules",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubscriptionSchedules",
                table: "SubscriptionSchedules",
                columns: new[] { "SubscriptionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_AggregationForId",
                table: "Subscriptions",
                column: "AggregationForId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ResponseSubscriptionId",
                table: "Subscriptions",
                column: "ResponseSubscriptionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_AggFor",
                table: "Subscriptions",
                column: "AggregationForId",
                principalTable: "Subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_RespSub",
                table: "Subscriptions",
                column: "ResponseSubscriptionId",
                principalTable: "Subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionSchedules_Subscriptions_SubscriptionId",
                table: "SubscriptionSchedules",
                column: "SubscriptionId",
                principalTable: "Subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_AggFor",
                table: "Subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_RespSub",
                table: "Subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionSchedules_Subscriptions_SubscriptionId",
                table: "SubscriptionSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_AggregationForId",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_ResponseSubscriptionId",
                table: "Subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubscriptionSchedules",
                table: "SubscriptionSchedules");

            migrationBuilder.RenameTable(
                name: "SubscriptionSchedules",
                newName: "SubscriptionReceiveSchedules");

            migrationBuilder.AlterColumn<double>(
                name: "On",
                table: "SubscriptionReceiveSchedules",
                type: "double",
                nullable: false,
                oldClrType: typeof(long));

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubscriptionReceiveSchedules",
                table: "SubscriptionReceiveSchedules",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "SubscriptionAggregationSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Backwards = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    On = table.Column<double>(type: "double", nullable: false),
                    Recurrence = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    SubscriptionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionAggregationSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionAggregationSchedules_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_AggregationForId",
                table: "Subscriptions",
                column: "AggregationForId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ResponseSubscriptionId",
                table: "Subscriptions",
                column: "ResponseSubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionReceiveSchedules_SubscriptionId",
                table: "SubscriptionReceiveSchedules",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAggregationSchedules_SubscriptionId",
                table: "SubscriptionAggregationSchedules",
                column: "SubscriptionId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionReceiveSchedules_Subscriptions_SubscriptionId",
                table: "SubscriptionReceiveSchedules",
                column: "SubscriptionId",
                principalTable: "Subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_Subscriptions_AggregationForId",
                table: "Subscriptions",
                column: "AggregationForId",
                principalTable: "Subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_Subscriptions_ResponseSubscriptionId",
                table: "Subscriptions",
                column: "ResponseSubscriptionId",
                principalTable: "Subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
