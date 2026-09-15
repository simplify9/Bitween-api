using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMustChangePassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                schema: "infolink",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Only where the password is still the one we ship.
            //
            // EF scaffolded a plain UpdateData here, because as far as the model is concerned the
            // seeded row simply gained a column value. That would have demanded a new password from
            // every installation on the next deploy, including the ones that chose theirs years ago
            // — a forced interruption for people who had already done the right thing.
            //
            // Matching on the stored hash narrows it to the installations the flag is actually for:
            // those still signing in with the password published in our public repository. On
            // anything else this updates no rows at all.
            migrationBuilder.Sql(
                """
                UPDATE infolink."Accounts"
                SET must_change_password = TRUE
                WHERE id = 9999 AND password = '$SWHASH$V1$10000$VQCi48eitH4Ml5juvBMOFZrMdQwBbhuIQVXe6RR7qJdDF2bJ';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "must_change_password",
                schema: "infolink",
                table: "Accounts");
        }
    }
}
