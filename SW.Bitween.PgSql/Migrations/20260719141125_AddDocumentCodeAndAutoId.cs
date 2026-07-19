using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentCodeAndAutoId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "id",
                schema: "infolink",
                table: "document",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "infolink",
                table: "document",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "document",
                keyColumn: "id",
                keyValue: 10001,
                column: "code",
                value: null);

            // Existing rows predate the code column; give them a derived value so the
            // (nullable, unique) index has no surprises and the local UI has something
            // sensible to show instead of a permanently blank code.
            migrationBuilder.Sql(@"
                UPDATE infolink.document
                SET code = upper(regexp_replace(trim(name), '[^a-zA-Z0-9]+', '_', 'g'))
                WHERE code IS NULL AND id <> 10001;");

            migrationBuilder.CreateIndex(
                name: "ix_document_code",
                schema: "infolink",
                table: "document",
                column: "code",
                unique: true);

            // The identity sequence starts at 1; realign it past the existing (user-assigned,
            // pre-identity) ids so the next INSERT doesn't eventually collide with them.
            migrationBuilder.Sql(@"
                SELECT setval(
                    pg_get_serial_sequence('infolink.document', 'id'),
                    (SELECT COALESCE(MAX(id), 0) FROM infolink.document));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_document_code",
                schema: "infolink",
                table: "document");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "infolink",
                table: "document");

            migrationBuilder.AlterColumn<int>(
                name: "id",
                schema: "infolink",
                table: "document",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
        }
    }
}
