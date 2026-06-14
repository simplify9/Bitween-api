using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class Quartz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_executions",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_name = table.Column<string>(type: "text", nullable: false),
                    job_group = table.Column<string>(type: "text", nullable: false),
                    job_type_name = table.Column<string>(type: "text", nullable: false),
                    fire_instance_id = table.Column<string>(type: "text", nullable: false),
                    start_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    node = table.Column<string>(type: "text", nullable: false),
                    context = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_executions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "qrtz_calendars",
                schema: "infolink",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "text", nullable: false),
                    calendar_name = table.Column<string>(type: "text", nullable: false),
                    calendar = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qrtz_calendars", x => new { x.sched_name, x.calendar_name });
                });

            migrationBuilder.CreateTable(
                name: "qrtz_fired_triggers",
                schema: "infolink",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "text", nullable: false),
                    entry_id = table.Column<string>(type: "text", nullable: false),
                    trigger_name = table.Column<string>(type: "text", nullable: false),
                    trigger_group = table.Column<string>(type: "text", nullable: false),
                    instance_name = table.Column<string>(type: "text", nullable: false),
                    fired_time = table.Column<long>(type: "bigint", nullable: false),
                    sched_time = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    job_name = table.Column<string>(type: "text", nullable: true),
                    job_group = table.Column<string>(type: "text", nullable: true),
                    is_nonconcurrent = table.Column<bool>(type: "bool", nullable: false),
                    requests_recovery = table.Column<bool>(type: "bool", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qrtz_fired_triggers", x => new { x.sched_name, x.entry_id });
                });

            migrationBuilder.CreateTable(
                name: "qrtz_job_details",
                schema: "infolink",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "text", nullable: false),
                    job_name = table.Column<string>(type: "text", nullable: false),
                    job_group = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    job_class_name = table.Column<string>(type: "text", nullable: false),
                    is_durable = table.Column<bool>(type: "bool", nullable: false),
                    is_nonconcurrent = table.Column<bool>(type: "bool", nullable: false),
                    is_update_data = table.Column<bool>(type: "bool", nullable: false),
                    requests_recovery = table.Column<bool>(type: "bool", nullable: false),
                    job_data = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qrtz_job_details", x => new { x.sched_name, x.job_name, x.job_group });
                });

            migrationBuilder.CreateTable(
                name: "qrtz_locks",
                schema: "infolink",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "text", nullable: false),
                    lock_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qrtz_locks", x => new { x.sched_name, x.lock_name });
                });

            migrationBuilder.CreateTable(
                name: "qrtz_paused_trigger_grps",
                schema: "infolink",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "text", nullable: false),
                    trigger_group = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qrtz_paused_trigger_grps", x => new { x.sched_name, x.trigger_group });
                });

            migrationBuilder.CreateTable(
                name: "qrtz_scheduler_state",
                schema: "infolink",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "text", nullable: false),
                    instance_name = table.Column<string>(type: "text", nullable: false),
                    last_checkin_time = table.Column<long>(type: "bigint", nullable: false),
                    checkin_interval = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qrtz_scheduler_state", x => new { x.sched_name, x.instance_name });
                });

            migrationBuilder.CreateTable(
                name: "qrtz_triggers",
                schema: "infolink",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "text", nullable: false),
                    trigger_name = table.Column<string>(type: "text", nullable: false),
                    trigger_group = table.Column<string>(type: "text", nullable: false),
                    job_name = table.Column<string>(type: "text", nullable: false),
                    job_group = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    next_fire_time = table.Column<long>(type: "bigint", nullable: true),
                    prev_fire_time = table.Column<long>(type: "bigint", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: true),
                    trigger_state = table.Column<string>(type: "text", nullable: false),
                    trigger_type = table.Column<string>(type: "text", nullable: false),
                    start_time = table.Column<long>(type: "bigint", nullable: false),
                    end_time = table.Column<long>(type: "bigint", nullable: true),
                    calendar_name = table.Column<string>(type: "text", nullable: true),
                    misfire_instr = table.Column<int>(type: "integer", nullable: true),
                    job_data = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qrtz_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "fk_qrtz_triggers_qrtz_job_details_sched_name_job_name_job_group",
                        columns: x => new { x.sched_name, x.job_name, x.job_group },
                        principalSchema: "infolink",
                        principalTable: "qrtz_job_details",
                        principalColumns: new[] { "sched_name", "job_name", "job_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qrtz_blob_triggers",
                schema: "infolink",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "text", nullable: false),
                    trigger_name = table.Column<string>(type: "text", nullable: false),
                    trigger_group = table.Column<string>(type: "text", nullable: false),
                    blob_data = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qrtz_blob_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "fk_qrtz_blob_triggers_qrtz_triggers_sched_name_trigger_name_tr",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalSchema: "infolink",
                        principalTable: "qrtz_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qrtz_cron_triggers",
                schema: "infolink",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "text", nullable: false),
                    trigger_name = table.Column<string>(type: "text", nullable: false),
                    trigger_group = table.Column<string>(type: "text", nullable: false),
                    cron_expression = table.Column<string>(type: "text", nullable: false),
                    time_zone_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qrtz_cron_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "fk_qrtz_cron_triggers_qrtz_triggers_sched_name_trigger_name_tr",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalSchema: "infolink",
                        principalTable: "qrtz_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qrtz_simple_triggers",
                schema: "infolink",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "text", nullable: false),
                    trigger_name = table.Column<string>(type: "text", nullable: false),
                    trigger_group = table.Column<string>(type: "text", nullable: false),
                    repeat_count = table.Column<long>(type: "bigint", nullable: false),
                    repeat_interval = table.Column<long>(type: "bigint", nullable: false),
                    times_triggered = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qrtz_simple_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "fk_qrtz_simple_triggers_qrtz_triggers_sched_name_trigger_name_",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalSchema: "infolink",
                        principalTable: "qrtz_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qrtz_simprop_triggers",
                schema: "infolink",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "text", nullable: false),
                    trigger_name = table.Column<string>(type: "text", nullable: false),
                    trigger_group = table.Column<string>(type: "text", nullable: false),
                    str_prop_1 = table.Column<string>(type: "text", nullable: true),
                    str_prop_2 = table.Column<string>(type: "text", nullable: true),
                    str_prop_3 = table.Column<string>(type: "text", nullable: true),
                    int_prop_1 = table.Column<int>(type: "integer", nullable: true),
                    int_prop_2 = table.Column<int>(type: "integer", nullable: true),
                    long_prop_1 = table.Column<long>(type: "bigint", nullable: true),
                    long_prop_2 = table.Column<long>(type: "bigint", nullable: true),
                    dec_prop_1 = table.Column<decimal>(type: "numeric", nullable: true),
                    dec_prop_2 = table.Column<decimal>(type: "numeric", nullable: true),
                    bool_prop_1 = table.Column<bool>(type: "bool", nullable: true),
                    bool_prop_2 = table.Column<bool>(type: "bool", nullable: true),
                    time_zone_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qrtz_simprop_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "fk_qrtz_simprop_triggers_qrtz_triggers_sched_name_trigger_name",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalSchema: "infolink",
                        principalTable: "qrtz_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_je_fire_instance_id",
                schema: "infolink",
                table: "job_executions",
                column: "fire_instance_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_je_group_name_start",
                schema: "infolink",
                table: "job_executions",
                columns: new[] { "job_group", "job_name", "start_time_utc" });

            migrationBuilder.CreateIndex(
                name: "idx_je_start_time",
                schema: "infolink",
                table: "job_executions",
                column: "start_time_utc");

            migrationBuilder.CreateIndex(
                name: "idx_je_success",
                schema: "infolink",
                table: "job_executions",
                column: "success");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_job_group",
                schema: "infolink",
                table: "qrtz_fired_triggers",
                column: "job_group");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_job_name",
                schema: "infolink",
                table: "qrtz_fired_triggers",
                column: "job_name");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_job_req_recovery",
                schema: "infolink",
                table: "qrtz_fired_triggers",
                column: "requests_recovery");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_trig_group",
                schema: "infolink",
                table: "qrtz_fired_triggers",
                column: "trigger_group");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_trig_inst_name",
                schema: "infolink",
                table: "qrtz_fired_triggers",
                column: "instance_name");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_trig_name",
                schema: "infolink",
                table: "qrtz_fired_triggers",
                column: "trigger_name");

            migrationBuilder.CreateIndex(
                name: "idx_qrtz_ft_trig_nm_gp",
                schema: "infolink",
                table: "qrtz_fired_triggers",
                columns: new[] { "sched_name", "trigger_name", "trigger_group" });

            migrationBuilder.CreateIndex(
                name: "idx_j_req_recovery",
                schema: "infolink",
                table: "qrtz_job_details",
                column: "requests_recovery");

            migrationBuilder.CreateIndex(
                name: "idx_t_next_fire_time",
                schema: "infolink",
                table: "qrtz_triggers",
                column: "next_fire_time");

            migrationBuilder.CreateIndex(
                name: "idx_t_nft_st",
                schema: "infolink",
                table: "qrtz_triggers",
                columns: new[] { "next_fire_time", "trigger_state" });

            migrationBuilder.CreateIndex(
                name: "idx_t_state",
                schema: "infolink",
                table: "qrtz_triggers",
                column: "trigger_state");

            migrationBuilder.CreateIndex(
                name: "ix_qrtz_triggers_sched_name_job_name_job_group",
                schema: "infolink",
                table: "qrtz_triggers",
                columns: new[] { "sched_name", "job_name", "job_group" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_executions",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "qrtz_blob_triggers",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "qrtz_calendars",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "qrtz_cron_triggers",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "qrtz_fired_triggers",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "qrtz_locks",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "qrtz_paused_trigger_grps",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "qrtz_scheduler_state",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "qrtz_simple_triggers",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "qrtz_simprop_triggers",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "qrtz_triggers",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "qrtz_job_details",
                schema: "infolink");
        }
    }
}
