using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentCodeAndAutoId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "DocumentIds");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValueSql: "NEXT VALUE FOR [DocumentIds]",
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Documents",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Documents",
                keyColumn: "Id",
                keyValue: 10001,
                column: "Code",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Code",
                table: "Documents",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            // The sequence starts at 1; realign it past the existing (user-assigned, pre-sequence)
            // ids so the next INSERT can't collide with them. RESTART WITH needs a literal, hence
            // the dynamic SQL.
            migrationBuilder.Sql("""
                DECLARE @next bigint = (SELECT ISNULL(MAX([Id]), 0) + 1 FROM [Documents]);
                DECLARE @sql nvarchar(200) =
                    N'ALTER SEQUENCE [DocumentIds] RESTART WITH ' + CAST(@next AS nvarchar(20));
                EXEC sp_executesql @sql;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_Code",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Documents");

            migrationBuilder.DropSequence(
                name: "DocumentIds");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Documents",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValueSql: "NEXT VALUE FOR [DocumentIds]");
        }
    }
}
