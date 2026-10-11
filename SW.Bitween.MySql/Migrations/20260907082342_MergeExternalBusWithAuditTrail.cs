using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class MergeExternalBusWithAuditTrail : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// Deliberately empty.
        ///
        /// Two branches added migrations at the same time - the audit trail on releases/r10.0, the
        /// external bus data sources here - so each side's model snapshot described only its own
        /// half. A snapshot is generated from the model, and hand-merging generated code is how one
        /// silently stops matching it, so the merge took r10's snapshot wholesale and let EF
        /// regenerate from the combined model.
        ///
        /// Regenerating produces this migration, whose Up() would create the data sources, the
        /// deduplication table and the cluster leases - all of which migrations already on this
        /// branch create. Running it would fail on a fresh database and do nothing on an existing
        /// one. What is worth keeping is the snapshot beside it, which now describes both halves.
        /// MigrationDriftTests is what proves that, and it is the reason this is safe to leave
        /// empty rather than delete.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
