using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class Quartz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_executions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    job_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    job_group = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    job_type_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fire_instance_id = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    start_time_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    end_time_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    success = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    error = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    node = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    context = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_executions", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRTZ_calendars",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    calendar_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    calendar = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_calendars", x => new { x.sched_name, x.calendar_name });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRTZ_fired_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entry_id = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_group = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    instance_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fired_time = table.Column<long>(type: "bigint", nullable: false),
                    sched_time = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    state = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    job_name = table.Column<string>(type: "varchar(200)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    job_group = table.Column<string>(type: "varchar(200)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_nonconcurrent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    requests_recovery = table.Column<bool>(type: "tinyint(1)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_fired_triggers", x => new { x.sched_name, x.entry_id });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRTZ_job_details",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    job_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    job_group = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(200)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    job_class_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_durable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_nonconcurrent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_update_data = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    requests_recovery = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    job_data = table.Column<byte[]>(type: "longblob", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_job_details", x => new { x.sched_name, x.job_name, x.job_group });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRTZ_locks",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lock_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_locks", x => new { x.sched_name, x.lock_name });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRTZ_paused_trigger_grps",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_group = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_paused_trigger_grps", x => new { x.sched_name, x.trigger_group });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRTZ_scheduler_state",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    instance_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_checkin_time = table.Column<long>(type: "bigint", nullable: false),
                    checkin_interval = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_scheduler_state", x => new { x.sched_name, x.instance_name });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRTZ_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_group = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    job_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    job_group = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(200)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    next_fire_time = table.Column<long>(type: "bigint", nullable: true),
                    prev_fire_time = table.Column<long>(type: "bigint", nullable: true),
                    priority = table.Column<int>(type: "int", nullable: true),
                    trigger_state = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_type = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    start_time = table.Column<long>(type: "bigint", nullable: false),
                    end_time = table.Column<long>(type: "bigint", nullable: true),
                    calendar_name = table.Column<string>(type: "varchar(200)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    misfire_instr = table.Column<int>(type: "int", nullable: true),
                    job_data = table.Column<byte[]>(type: "longblob", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_QRTZ_triggers_QRTZ_job_details_sched_name_job_name_job_group",
                        columns: x => new { x.sched_name, x.job_name, x.job_group },
                        principalTable: "QRTZ_job_details",
                        principalColumns: new[] { "sched_name", "job_name", "job_group" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRTZ_blob_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_group = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    blob_data = table.Column<byte[]>(type: "longblob", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_blob_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_QRTZ_blob_triggers_QRTZ_triggers_sched_name_trigger_name_tri~",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalTable: "QRTZ_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRTZ_cron_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_group = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    cron_expression = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    time_zone_id = table.Column<string>(type: "varchar(200)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_cron_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_QRTZ_cron_triggers_QRTZ_triggers_sched_name_trigger_name_tri~",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalTable: "QRTZ_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRTZ_simple_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_group = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    repeat_count = table.Column<long>(type: "bigint", nullable: false),
                    repeat_interval = table.Column<long>(type: "bigint", nullable: false),
                    times_triggered = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_simple_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_QRTZ_simple_triggers_QRTZ_triggers_sched_name_trigger_name_t~",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalTable: "QRTZ_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QRTZ_simprop_triggers",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_name = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trigger_group = table.Column<string>(type: "varchar(200)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    str_prop_1 = table.Column<string>(type: "varchar(200)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    str_prop_2 = table.Column<string>(type: "varchar(200)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    str_prop_3 = table.Column<string>(type: "varchar(200)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    int_prop_1 = table.Column<int>(type: "int", nullable: true),
                    int_prop_2 = table.Column<int>(type: "int", nullable: true),
                    long_prop_1 = table.Column<long>(type: "bigint", nullable: true),
                    long_prop_2 = table.Column<long>(type: "bigint", nullable: true),
                    dec_prop_1 = table.Column<decimal>(type: "numeric(65,30)", nullable: true),
                    dec_prop_2 = table.Column<decimal>(type: "numeric(65,30)", nullable: true),
                    bool_prop_1 = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    bool_prop_2 = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    time_zone_id = table.Column<string>(type: "varchar(200)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_simprop_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_QRTZ_simprop_triggers_QRTZ_triggers_sched_name_trigger_name_~",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalTable: "QRTZ_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "idx_je_fire_instance_id",
                table: "job_executions",
                column: "fire_instance_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_je_group_name_start",
                table: "job_executions",
                columns: new[] { "job_group", "job_name", "start_time_utc" });

            migrationBuilder.CreateIndex(
                name: "idx_je_start_time",
                table: "job_executions",
                column: "start_time_utc");

            migrationBuilder.CreateIndex(
                name: "idx_je_success",
                table: "job_executions",
                column: "success");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_job_group",
                table: "QRTZ_fired_triggers",
                column: "job_group");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_job_name",
                table: "QRTZ_fired_triggers",
                column: "job_name");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_job_req_recovery",
                table: "QRTZ_fired_triggers",
                column: "requests_recovery");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_trig_group",
                table: "QRTZ_fired_triggers",
                column: "trigger_group");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_trig_inst_name",
                table: "QRTZ_fired_triggers",
                column: "instance_name");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_trig_name",
                table: "QRTZ_fired_triggers",
                column: "trigger_name");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_trig_nm_gp",
                table: "QRTZ_fired_triggers",
                columns: new[] { "sched_name", "trigger_name", "trigger_group" });

            migrationBuilder.CreateIndex(
                name: "idx_j_req_recovery",
                table: "QRTZ_job_details",
                column: "requests_recovery");

            migrationBuilder.CreateIndex(
                name: "idx_t_next_fire_time",
                table: "QRTZ_triggers",
                column: "next_fire_time");

            migrationBuilder.CreateIndex(
                name: "idx_t_nft_st",
                table: "QRTZ_triggers",
                columns: new[] { "next_fire_time", "trigger_state" });

            migrationBuilder.CreateIndex(
                name: "idx_t_state",
                table: "QRTZ_triggers",
                column: "trigger_state");

            migrationBuilder.CreateIndex(
                name: "IX_QRTZ_triggers_sched_name_job_name_job_group",
                table: "QRTZ_triggers",
                columns: new[] { "sched_name", "job_name", "job_group" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_executions");

            migrationBuilder.DropTable(
                name: "QRTZ_blob_triggers");

            migrationBuilder.DropTable(
                name: "QRTZ_calendars");

            migrationBuilder.DropTable(
                name: "QRTZ_cron_triggers");

            migrationBuilder.DropTable(
                name: "QRTZ_fired_triggers");

            migrationBuilder.DropTable(
                name: "QRTZ_locks");

            migrationBuilder.DropTable(
                name: "QRTZ_paused_trigger_grps");

            migrationBuilder.DropTable(
                name: "QRTZ_scheduler_state");

            migrationBuilder.DropTable(
                name: "QRTZ_simple_triggers");

            migrationBuilder.DropTable(
                name: "QRTZ_simprop_triggers");

            migrationBuilder.DropTable(
                name: "QRTZ_triggers");

            migrationBuilder.DropTable(
                name: "QRTZ_job_details");
        }
    }
}
