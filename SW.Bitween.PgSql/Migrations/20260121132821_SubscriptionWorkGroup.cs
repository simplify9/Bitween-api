using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SW.Bitween.Domain;
using SW.Bitween.Model;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    public partial class SubscriptionWorkGroup : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1️⃣ Rename schema (atomic & safe in PostgreSQL)
        migrationBuilder.Sql("""
            ALTER SCHEMA infolink RENAME TO bitween;
        """);

        // 2️⃣ Add column
        migrationBuilder.AddColumn<int>(
            name: "work_group_id",
            schema: "bitween",
            table: "subscription",
            type: "integer",
            nullable: true);

        // 3️⃣ Create new table
        migrationBuilder.CreateTable(
            name: "work_group",
            schema: "bitween",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                name = table.Column<string>(type: "text", nullable: true),
                bus_message_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                options = table.Column<WorkGroupOptions>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_work_group", x => x.id);
            });

        // 4️⃣ Index + FK
        migrationBuilder.CreateIndex(
            name: "ix_subscription_work_group_id",
            schema: "bitween",
            table: "subscription",
            column: "work_group_id");

        migrationBuilder.AddForeignKey(
            name: "fk_subscription_work_group_work_group_id",
            schema: "bitween",
            table: "subscription",
            column: "work_group_id",
            principalSchema: "bitween",
            principalTable: "work_group",
            principalColumn: "id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "work_group",
            schema: "bitween");

        migrationBuilder.DropIndex(
            name: "ix_subscription_work_group_id",
            schema: "bitween",
            table: "subscription");

        migrationBuilder.DropColumn(
            name: "work_group_id",
            schema: "bitween",
            table: "subscription");

        migrationBuilder.Sql("""
            ALTER SCHEMA bitween RENAME TO infolink;
        """);
    }
}

    /// <inheritdoc />
    public partial class SubscriptionWoqrkGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_xchange_aggregation_xchange_xchange_id",
                schema: "infolink",
                table: "xchange_aggregation");

            migrationBuilder.DropForeignKey(
                name: "fk_xchange_delivery_xchange_xchange_id",
                schema: "infolink",
                table: "xchange_delivery");

            migrationBuilder.DropForeignKey(
                name: "fk_xchange_promoted_properties_xchange_xchange_id",
                schema: "infolink",
                table: "xchange_promoted_properties");

            migrationBuilder.DropForeignKey(
                name: "fk_xchange_result_xchange_xchange_id",
                schema: "infolink",
                table: "xchange_result");

            migrationBuilder.EnsureSchema(
                name: "bitween");

            migrationBuilder.RenameTable(
                name: "xchange_result",
                schema: "infolink",
                newName: "xchange_result",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "xchange_promoted_properties",
                schema: "infolink",
                newName: "xchange_promoted_properties",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "xchange_notification",
                schema: "infolink",
                newName: "xchange_notification",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "xchange_delivery",
                schema: "infolink",
                newName: "xchange_delivery",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "xchange_aggregation",
                schema: "infolink",
                newName: "xchange_aggregation",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "xchange",
                schema: "infolink",
                newName: "xchange",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "subscription_trail",
                schema: "infolink",
                newName: "subscription_trail",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "subscription_schedule",
                schema: "infolink",
                newName: "subscription_schedule",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "subscription_category",
                schema: "infolink",
                newName: "subscription_category",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "subscription",
                schema: "infolink",
                newName: "subscription",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "running_result",
                schema: "infolink",
                newName: "running_result",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "RefreshTokens",
                schema: "infolink",
                newName: "RefreshTokens",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "partner_api_credential",
                schema: "infolink",
                newName: "partner_api_credential",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "partner",
                schema: "infolink",
                newName: "partner",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "on_hold_xchange",
                schema: "infolink",
                newName: "on_hold_xchange",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "notifier",
                schema: "infolink",
                newName: "notifier",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "document_trail",
                schema: "infolink",
                newName: "document_trail",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "document",
                schema: "infolink",
                newName: "document",
                newSchema: "bitween");

            migrationBuilder.RenameTable(
                name: "Accounts",
                schema: "infolink",
                newName: "Accounts",
                newSchema: "bitween");

            migrationBuilder.AddColumn<int>(
                name: "work_group_id",
                schema: "bitween",
                table: "subscription",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "work_group",
                schema: "bitween",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: true),
                    bus_message_name = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: false),
                    options = table.Column<WorkGroupOptions>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_group", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_work_group_id",
                schema: "bitween",
                table: "subscription",
                column: "work_group_id");

            migrationBuilder.AddForeignKey(
                name: "fk_subscription_work_group_work_group_id",
                schema: "bitween",
                table: "subscription",
                column: "work_group_id",
                principalSchema: "bitween",
                principalTable: "work_group",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_xchange_aggregation_xchange_id",
                schema: "bitween",
                table: "xchange_aggregation",
                column: "id",
                principalSchema: "bitween",
                principalTable: "xchange",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_xchange_delivery_xchange_id",
                schema: "bitween",
                table: "xchange_delivery",
                column: "id",
                principalSchema: "bitween",
                principalTable: "xchange",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_xchange_promoted_properties_xchange_id",
                schema: "bitween",
                table: "xchange_promoted_properties",
                column: "id",
                principalSchema: "bitween",
                principalTable: "xchange",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_xchange_result_xchange_id",
                schema: "bitween",
                table: "xchange_result",
                column: "id",
                principalSchema: "bitween",
                principalTable: "xchange",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_subscription_work_group_work_group_id",
                schema: "bitween",
                table: "subscription");

            migrationBuilder.DropForeignKey(
                name: "fk_xchange_aggregation_xchange_id",
                schema: "bitween",
                table: "xchange_aggregation");

            migrationBuilder.DropForeignKey(
                name: "fk_xchange_delivery_xchange_id",
                schema: "bitween",
                table: "xchange_delivery");

            migrationBuilder.DropForeignKey(
                name: "fk_xchange_promoted_properties_xchange_id",
                schema: "bitween",
                table: "xchange_promoted_properties");

            migrationBuilder.DropForeignKey(
                name: "fk_xchange_result_xchange_id",
                schema: "bitween",
                table: "xchange_result");
            
            migrationBuilder.RenameTable(
                name: "xchange_result",
                schema: "bitween",
                newName: "xchange_result",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "xchange_promoted_properties",
                schema: "bitween",
                newName: "xchange_promoted_properties",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "xchange_notification",
                schema: "bitween",
                newName: "xchange_notification",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "xchange_delivery",
                schema: "bitween",
                newName: "xchange_delivery",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "xchange_aggregation",
                schema: "bitween",
                newName: "xchange_aggregation",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "xchange",
                schema: "bitween",
                newName: "xchange",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "subscription_trail",
                schema: "bitween",
                newName: "subscription_trail",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "subscription_schedule",
                schema: "bitween",
                newName: "subscription_schedule",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "subscription_category",
                schema: "bitween",
                newName: "subscription_category",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "subscription",
                schema: "bitween",
                newName: "subscription",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "running_result",
                schema: "bitween",
                newName: "running_result",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "RefreshTokens",
                schema: "bitween",
                newName: "RefreshTokens",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "partner_api_credential",
                schema: "bitween",
                newName: "partner_api_credential",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "partner",
                schema: "bitween",
                newName: "partner",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "on_hold_xchange",
                schema: "bitween",
                newName: "on_hold_xchange",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "notifier",
                schema: "bitween",
                newName: "notifier",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "document_trail",
                schema: "bitween",
                newName: "document_trail",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "document",
                schema: "bitween",
                newName: "document",
                newSchema: "infolink");

            migrationBuilder.RenameTable(
                name: "Accounts",
                schema: "bitween",
                newName: "Accounts",
                newSchema: "infolink");

            migrationBuilder.AddForeignKey(
                name: "fk_xchange_aggregation_xchange_xchange_id",
                schema: "infolink",
                table: "xchange_aggregation",
                column: "id",
                principalSchema: "infolink",
                principalTable: "xchange",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_xchange_delivery_xchange_xchange_id",
                schema: "infolink",
                table: "xchange_delivery",
                column: "id",
                principalSchema: "infolink",
                principalTable: "xchange",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_xchange_promoted_properties_xchange_xchange_id",
                schema: "infolink",
                table: "xchange_promoted_properties",
                column: "id",
                principalSchema: "infolink",
                principalTable: "xchange",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_xchange_result_xchange_xchange_id",
                schema: "infolink",
                table: "xchange_result",
                column: "id",
                principalSchema: "infolink",
                principalTable: "xchange",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
