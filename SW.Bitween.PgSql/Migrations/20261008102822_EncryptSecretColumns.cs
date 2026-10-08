using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class EncryptSecretColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. The credential columns gained an encrypting converter, which
            // changes how EF compares the SYSTEM partner's seed row, so it proposed setting that
            // row's adapter_properties to null. The schema is unchanged, and running that update
            // would wipe any properties a deployment has set on the SYSTEM partner. This migration
            // exists only so the snapshot records the converted model.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
