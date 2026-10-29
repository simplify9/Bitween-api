using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Connections",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: false),
                    grant = table.Column<int>(type: "integer", nullable: false),
                    authorize_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    token_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    client_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    client_secret = table.Column<string>(type: "text", nullable: true),
                    scopes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    authorize_parameters = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    token_auth = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    username = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    password = table.Column<string>(type: "text", nullable: true),
                    refresh_token = table.Column<string>(type: "text", nullable: true),
                    access_token = table.Column<string>(type: "text", nullable: true),
                    access_token_expires_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    renewing_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    authorized_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    authorized_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    modified_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connections", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_connections_name",
                schema: "infolink",
                table: "Connections",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Connections",
                schema: "infolink");
        }
    }
}
