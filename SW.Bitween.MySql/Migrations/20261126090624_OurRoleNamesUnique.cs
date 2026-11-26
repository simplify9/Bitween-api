using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class OurRoleNamesUnique : Migration
    {
        /// <inheritdoc />
        // MySQL counts every empty partner as different and has no filtered index, so our own roles'
        // names are kept unique through a key that reads 0 for them; a partner's own keep theirs.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `Roles` ADD COLUMN `OwnerKey` int AS (IFNULL(`PartnerId`, 0)) STORED;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX `IX_Roles_OwnerKey_Name` ON `Roles` (`OwnerKey`, `Name`);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX `IX_Roles_OwnerKey_Name` ON `Roles`;");
            migrationBuilder.Sql("ALTER TABLE `Roles` DROP COLUMN `OwnerKey`;");
        }
    }
}
