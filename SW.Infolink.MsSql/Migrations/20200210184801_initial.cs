using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MsSql.Migrations
{
    public partial class initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "infolink");

            migrationBuilder.CreateTable(
                name: "AccessKeySets",
                schema: "infolink",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(unicode: false, maxLength: 200, nullable: false),
                    Key1 = table.Column<string>(unicode: false, maxLength: 1024, nullable: false),
                    Key2 = table.Column<string>(unicode: false, maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessKeySets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Adapters",
                schema: "infolink",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(nullable: false),
                    Name = table.Column<string>(unicode: false, maxLength: 200, nullable: false),
                    DocumentId = table.Column<int>(nullable: false),
                    Hash = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
                    Description = table.Column<string>(nullable: true),
                    Timeout = table.Column<int>(nullable: false),
                    Package = table.Column<byte[]>(nullable: true),
                    Properties = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adapters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                schema: "infolink",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    BusEnabled = table.Column<bool>(nullable: false),
                    BusMessageTypeName = table.Column<string>(unicode: false, maxLength: 500, nullable: true),
                    DuplicateInterval = table.Column<int>(nullable: false),
                    PromotedProperties = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Receivers",
                schema: "infolink",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    ReceiverId = table.Column<int>(nullable: false),
                    Properties = table.Column<string>(nullable: true),
                    ReceiveOn = table.Column<DateTime>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receivers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscribers",
                schema: "infolink",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    KeySetId = table.Column<int>(nullable: false),
                    DocumentId = table.Column<int>(nullable: false),
                    HandlerId = table.Column<int>(nullable: false),
                    MapperId = table.Column<int>(nullable: false),
                    Temporary = table.Column<bool>(nullable: false),
                    Aggregate = table.Column<bool>(nullable: false),
                    Properties = table.Column<string>(nullable: true),
                    DocumentFilter = table.Column<string>(nullable: true),
                    Inactive = table.Column<bool>(nullable: false),
                    ResponseSubscriberId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscribers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "XchangeFiles",
                schema: "infolink",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false),
                    Type = table.Column<byte>(nullable: false),
                    Content = table.Column<byte[]>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeFiles", x => new { x.Id, x.Type });
                });

            migrationBuilder.CreateTable(
                name: "Xchanges",
                schema: "infolink",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscriberId = table.Column<int>(nullable: false),
                    DocumentId = table.Column<int>(nullable: false),
                    HandlerId = table.Column<int>(nullable: false),
                    MapperId = table.Column<int>(nullable: false),
                    References = table.Column<string>(maxLength: 1024, nullable: true),
                    Status = table.Column<int>(nullable: false),
                    Exception = table.Column<string>(nullable: true),
                    DeliveredOn = table.Column<DateTime>(nullable: true),
                    FinishedOn = table.Column<DateTime>(nullable: true),
                    StartedOn = table.Column<DateTime>(nullable: false),
                    InputFileName = table.Column<string>(maxLength: 200, nullable: true),
                    InputFileSize = table.Column<int>(nullable: false),
                    InputFileHash = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
                    DeliverOn = table.Column<DateTime>(nullable: true),
                    ResponseXchangeId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Xchanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReceiverSchedules",
                schema: "infolink",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Recurrence = table.Column<byte>(nullable: false),
                    On = table.Column<TimeSpan>(nullable: false),
                    Backwards = table.Column<bool>(nullable: false),
                    ReceiverId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiverSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiverSchedules_Receivers_ReceiverId",
                        column: x => x.ReceiverId,
                        principalSchema: "infolink",
                        principalTable: "Receivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriberSchedules",
                schema: "infolink",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Recurrence = table.Column<byte>(nullable: false),
                    On = table.Column<TimeSpan>(nullable: false),
                    Backwards = table.Column<bool>(nullable: false),
                    SubscriberId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriberSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriberSchedules_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalSchema: "infolink",
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiverSchedules_ReceiverId",
                schema: "infolink",
                table: "ReceiverSchedules",
                column: "ReceiverId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriberSchedules_SubscriberId",
                schema: "infolink",
                table: "SubscriberSchedules",
                column: "SubscriberId");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_DeliverOn",
                schema: "infolink",
                table: "Xchanges",
                column: "DeliverOn");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_DeliveredOn",
                schema: "infolink",
                table: "Xchanges",
                column: "DeliveredOn");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_InputFileHash",
                schema: "infolink",
                table: "Xchanges",
                column: "InputFileHash");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_SubscriberId",
                schema: "infolink",
                table: "Xchanges",
                column: "SubscriberId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessKeySets",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "Adapters",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "Documents",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "ReceiverSchedules",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "SubscriberSchedules",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "XchangeFiles",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "Xchanges",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "Receivers",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "Subscribers",
                schema: "infolink");
        }
    }
}
