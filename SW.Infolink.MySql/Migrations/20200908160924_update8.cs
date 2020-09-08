using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update8 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Xchanges_InputFileHash",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "InputFileHash",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "InputFileName",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "InputFileSize",
                table: "Xchanges");

            migrationBuilder.AddColumn<string>(
                name: "InputHash",
                table: "Xchanges",
                unicode: false,
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InputName",
                table: "Xchanges",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputSize",
                table: "Xchanges",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OutputHash",
                table: "XchangeResults",
                unicode: false,
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OutputName",
                table: "XchangeResults",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputSize",
                table: "XchangeResults",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResponseHash",
                table: "XchangeResults",
                unicode: false,
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResponseName",
                table: "XchangeResults",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponseSize",
                table: "XchangeResults",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_InputHash",
                table: "Xchanges",
                column: "InputHash");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Xchanges_InputHash",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "InputHash",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "InputName",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "InputSize",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "OutputHash",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "OutputName",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "OutputSize",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "ResponseHash",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "ResponseName",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "ResponseSize",
                table: "XchangeResults");

            migrationBuilder.AddColumn<string>(
                name: "InputFileHash",
                table: "Xchanges",
                type: "varchar(50) CHARACTER SET utf8mb4",
                unicode: false,
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InputFileName",
                table: "Xchanges",
                type: "varchar(200) CHARACTER SET utf8mb4",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputFileSize",
                table: "Xchanges",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_InputFileHash",
                table: "Xchanges",
                column: "InputFileHash");
        }
    }
}
