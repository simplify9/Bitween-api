using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class adapterseed : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Adapters",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Adapters",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Adapters",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Adapters",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Adapters",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Adapters",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Adapters",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Adapters",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Adapters",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Adapters",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "Adapters",
                keyColumn: "Id",
                keyValue: 11);
        }
    }
}
