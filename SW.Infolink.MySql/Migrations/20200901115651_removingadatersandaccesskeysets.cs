using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class removingadatersandaccesskeysets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessKeySets");

            migrationBuilder.DropTable(
                name: "Adapters");

            migrationBuilder.DropTable(
                name: "XchangeFiles");

            migrationBuilder.DropColumn(
                name: "KeySetId",
                table: "Subscribers");

            migrationBuilder.AlterColumn<string>(
                name: "MapperId",
                table: "Xchanges",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "HandlerId",
                table: "Xchanges",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "MapperId",
                table: "Subscribers",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "HandlerId",
                table: "Subscribers",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "ReceiverId",
                table: "Receivers",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "MapperId",
                table: "Xchanges",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "HandlerId",
                table: "Xchanges",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "MapperId",
                table: "Subscribers",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "HandlerId",
                table: "Subscribers",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KeySetId",
                table: "Subscribers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "ReceiverId",
                table: "Receivers",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "AccessKeySets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Key1 = table.Column<string>(type: "varchar(1024) CHARACTER SET utf8mb4", unicode: false, maxLength: 1024, nullable: false),
                    Key2 = table.Column<string>(type: "varchar(1024) CHARACTER SET utf8mb4", unicode: false, maxLength: 1024, nullable: false),
                    Name = table.Column<string>(type: "varchar(200) CHARACTER SET utf8mb4", unicode: false, maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessKeySets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Adapters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Description = table.Column<string>(type: "longtext CHARACTER SET utf8mb4", nullable: true),
                    Name = table.Column<string>(type: "varchar(200) CHARACTER SET utf8mb4", unicode: false, maxLength: 200, nullable: false),
                    Properties = table.Column<string>(type: "longtext CHARACTER SET utf8mb4", nullable: true),
                    ServerlessId = table.Column<string>(type: "varchar(200) CHARACTER SET utf8mb4", unicode: false, maxLength: 200, nullable: true),
                    Timeout = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adapters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "XchangeFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Content = table.Column<byte[]>(type: "longblob", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeFiles", x => new { x.Id, x.Type });
                });

            migrationBuilder.InsertData(
                table: "Adapters",
                columns: new[] { "Id", "Description", "Name", "Properties", "ServerlessId", "Timeout", "Type" },
                values: new object[,]
                {
                    { 1, null, "JsonToCsvMapper", "{}", "infolink.jsontocsvmapper", 0, 1 },
                    { 2, null, "JsonToXmlMapper", "{}", "infolink.jsontoxmlmapper", 0, 1 },
                    { 3, null, "As2FileHandler", "{}", "infolink.as2filehandler", 0, 0 },
                    { 4, null, "FtpFileHandler", "{}", "infolink.ftpfilehandler", 0, 0 },
                    { 5, null, "HttpFileHandler", "{}", "infolink.httpfilehandler", 0, 0 },
                    { 6, null, "SftpFileHandler", "{}", "infolink.sftpfilehandler", 0, 0 },
                    { 7, null, "S3FileHandler", "{}", "infolink.s3filehandler", 0, 0 },
                    { 8, null, "AzureBlobFileHandler", "{}", "infolink.azureblobfilehandler", 0, 0 },
                    { 9, null, "AzureBlobFileReceiver", "{}", "infolink.azureBlobfilereceiver", 0, 2 },
                    { 10, null, "SftpFileReceiver", "{}", "infolink.sftpfilereceiver", 0, 2 },
                    { 11, null, "FtpFileReceiver", "{}", "infolink.ftpfilereceiver", 0, 2 }
                });
        }
    }
}
