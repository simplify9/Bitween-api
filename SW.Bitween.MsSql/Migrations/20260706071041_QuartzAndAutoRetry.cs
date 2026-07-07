using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class QuartzAndAutoRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.AddColumn<string>(
                name: "GroupAttemptCounts",
                table: "Xchanges",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomRetryPolicy",
                table: "Subscriptions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryPolicyId",
                table: "Subscriptions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DelayedRetries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    On = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GroupAttemptCounts = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelayedRetries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "job_executions",
                schema: "dbo",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    job_group = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    job_type_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    fire_instance_id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    start_time_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    end_time_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    success = table.Column<bool>(type: "bit", nullable: true),
                    error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    node = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    context = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_executions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "QRTZ_calendars",
                schema: "dbo",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    calendar_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    calendar = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_calendars", x => new { x.sched_name, x.calendar_name });
                });

            migrationBuilder.CreateTable(
                name: "QRTZ_fired_triggers",
                schema: "dbo",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    entry_id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_group = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    instance_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    fired_time = table.Column<long>(type: "bigint", nullable: false),
                    sched_time = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    state = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    job_name = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    job_group = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    is_nonconcurrent = table.Column<bool>(type: "bit", nullable: false),
                    requests_recovery = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_fired_triggers", x => new { x.sched_name, x.entry_id });
                });

            migrationBuilder.CreateTable(
                name: "QRTZ_job_details",
                schema: "dbo",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    job_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    job_group = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    description = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    job_class_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    is_durable = table.Column<bool>(type: "bit", nullable: false),
                    is_nonconcurrent = table.Column<bool>(type: "bit", nullable: false),
                    is_update_data = table.Column<bool>(type: "bit", nullable: false),
                    requests_recovery = table.Column<bool>(type: "bit", nullable: false),
                    job_data = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_job_details", x => new { x.sched_name, x.job_name, x.job_group });
                });

            migrationBuilder.CreateTable(
                name: "QRTZ_locks",
                schema: "dbo",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    lock_name = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_locks", x => new { x.sched_name, x.lock_name });
                });

            migrationBuilder.CreateTable(
                name: "QRTZ_paused_trigger_grps",
                schema: "dbo",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_group = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_paused_trigger_grps", x => new { x.sched_name, x.trigger_group });
                });

            migrationBuilder.CreateTable(
                name: "QRTZ_scheduler_state",
                schema: "dbo",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    instance_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    last_checkin_time = table.Column<long>(type: "bigint", nullable: false),
                    checkin_interval = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_scheduler_state", x => new { x.sched_name, x.instance_name });
                });

            migrationBuilder.CreateTable(
                name: "RetryPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Groups = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetryPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QRTZ_triggers",
                schema: "dbo",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_group = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    job_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    job_group = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    description = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    next_fire_time = table.Column<long>(type: "bigint", nullable: true),
                    prev_fire_time = table.Column<long>(type: "bigint", nullable: true),
                    priority = table.Column<int>(type: "int", nullable: true),
                    trigger_state = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_type = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    start_time = table.Column<long>(type: "bigint", nullable: false),
                    end_time = table.Column<long>(type: "bigint", nullable: true),
                    calendar_name = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    misfire_instr = table.Column<int>(type: "int", nullable: true),
                    job_data = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_QRTZ_triggers_QRTZ_job_details_sched_name_job_name_job_group",
                        columns: x => new { x.sched_name, x.job_name, x.job_group },
                        principalSchema: "dbo",
                        principalTable: "QRTZ_job_details",
                        principalColumns: new[] { "sched_name", "job_name", "job_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QRTZ_blob_triggers",
                schema: "dbo",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_group = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    blob_data = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_blob_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_QRTZ_blob_triggers_QRTZ_triggers_sched_name_trigger_name_trigger_group",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalSchema: "dbo",
                        principalTable: "QRTZ_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QRTZ_cron_triggers",
                schema: "dbo",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_group = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    cron_expression = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    time_zone_id = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_cron_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_QRTZ_cron_triggers_QRTZ_triggers_sched_name_trigger_name_trigger_group",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalSchema: "dbo",
                        principalTable: "QRTZ_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QRTZ_simple_triggers",
                schema: "dbo",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_group = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    repeat_count = table.Column<long>(type: "bigint", nullable: false),
                    repeat_interval = table.Column<long>(type: "bigint", nullable: false),
                    times_triggered = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_simple_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_QRTZ_simple_triggers_QRTZ_triggers_sched_name_trigger_name_trigger_group",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalSchema: "dbo",
                        principalTable: "QRTZ_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QRTZ_simprop_triggers",
                schema: "dbo",
                columns: table => new
                {
                    sched_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    trigger_group = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    str_prop_1 = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    str_prop_2 = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    str_prop_3 = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    int_prop_1 = table.Column<int>(type: "int", nullable: true),
                    int_prop_2 = table.Column<int>(type: "int", nullable: true),
                    long_prop_1 = table.Column<long>(type: "bigint", nullable: true),
                    long_prop_2 = table.Column<long>(type: "bigint", nullable: true),
                    dec_prop_1 = table.Column<decimal>(type: "numeric(18,0)", nullable: true),
                    dec_prop_2 = table.Column<decimal>(type: "numeric(18,0)", nullable: true),
                    bool_prop_1 = table.Column<bool>(type: "bit", nullable: true),
                    bool_prop_2 = table.Column<bool>(type: "bit", nullable: true),
                    time_zone_id = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QRTZ_simprop_triggers", x => new { x.sched_name, x.trigger_name, x.trigger_group });
                    table.ForeignKey(
                        name: "FK_QRTZ_simprop_triggers_QRTZ_triggers_sched_name_trigger_name_trigger_group",
                        columns: x => new { x.sched_name, x.trigger_name, x.trigger_group },
                        principalSchema: "dbo",
                        principalTable: "QRTZ_triggers",
                        principalColumns: new[] { "sched_name", "trigger_name", "trigger_group" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_RetryPolicyId",
                table: "Subscriptions",
                column: "RetryPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_DelayedRetries_On",
                table: "DelayedRetries",
                column: "On");

            migrationBuilder.CreateIndex(
                name: "idx_je_fire_instance_id",
                schema: "dbo",
                table: "job_executions",
                column: "fire_instance_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_je_group_name_start",
                schema: "dbo",
                table: "job_executions",
                columns: new[] { "job_group", "job_name", "start_time_utc" });

            migrationBuilder.CreateIndex(
                name: "idx_je_start_time",
                schema: "dbo",
                table: "job_executions",
                column: "start_time_utc");

            migrationBuilder.CreateIndex(
                name: "idx_je_success",
                schema: "dbo",
                table: "job_executions",
                column: "success");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_job_group",
                schema: "dbo",
                table: "QRTZ_fired_triggers",
                column: "job_group");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_job_name",
                schema: "dbo",
                table: "QRTZ_fired_triggers",
                column: "job_name");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_job_req_recovery",
                schema: "dbo",
                table: "QRTZ_fired_triggers",
                column: "requests_recovery");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_trig_group",
                schema: "dbo",
                table: "QRTZ_fired_triggers",
                column: "trigger_group");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_trig_inst_name",
                schema: "dbo",
                table: "QRTZ_fired_triggers",
                column: "instance_name");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_trig_name",
                schema: "dbo",
                table: "QRTZ_fired_triggers",
                column: "trigger_name");

            migrationBuilder.CreateIndex(
                name: "idx_QRTZ_ft_trig_nm_gp",
                schema: "dbo",
                table: "QRTZ_fired_triggers",
                columns: new[] { "sched_name", "trigger_name", "trigger_group" });

            migrationBuilder.CreateIndex(
                name: "idx_j_req_recovery",
                schema: "dbo",
                table: "QRTZ_job_details",
                column: "requests_recovery");

            migrationBuilder.CreateIndex(
                name: "idx_t_next_fire_time",
                schema: "dbo",
                table: "QRTZ_triggers",
                column: "next_fire_time");

            migrationBuilder.CreateIndex(
                name: "idx_t_nft_st",
                schema: "dbo",
                table: "QRTZ_triggers",
                columns: new[] { "next_fire_time", "trigger_state" });

            migrationBuilder.CreateIndex(
                name: "idx_t_state",
                schema: "dbo",
                table: "QRTZ_triggers",
                column: "trigger_state");

            migrationBuilder.CreateIndex(
                name: "IX_QRTZ_triggers_sched_name_job_name_job_group",
                schema: "dbo",
                table: "QRTZ_triggers",
                columns: new[] { "sched_name", "job_name", "job_group" });

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_RetryPolicies_RetryPolicyId",
                table: "Subscriptions",
                column: "RetryPolicyId",
                principalTable: "RetryPolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_RetryPolicies_RetryPolicyId",
                table: "Subscriptions");

            migrationBuilder.DropTable(
                name: "DelayedRetries");

            migrationBuilder.DropTable(
                name: "job_executions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "QRTZ_blob_triggers",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "QRTZ_calendars",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "QRTZ_cron_triggers",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "QRTZ_fired_triggers",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "QRTZ_locks",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "QRTZ_paused_trigger_grps",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "QRTZ_scheduler_state",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "QRTZ_simple_triggers",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "QRTZ_simprop_triggers",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "RetryPolicies");

            migrationBuilder.DropTable(
                name: "QRTZ_triggers",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "QRTZ_job_details",
                schema: "dbo");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_RetryPolicyId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "GroupAttemptCounts",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "CustomRetryPolicy",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "RetryPolicyId",
                table: "Subscriptions");
        }
    }
}
