using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MsSql.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false),
                    Name = table.Column<string>(unicode: false, maxLength: 100, nullable: false),
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
                name: "Partners",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(unicode: false, maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Xchanges",
                columns: table => new
                {
                    Id = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
                    SubscriptionId = table.Column<int>(nullable: true),
                    DocumentId = table.Column<int>(nullable: false),
                    HandlerId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    MapperId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    HandlerProperties = table.Column<string>(nullable: true),
                    MapperProperties = table.Column<string>(nullable: true),
                    References = table.Column<string>(maxLength: 1024, nullable: true),
                    StartedOn = table.Column<DateTime>(nullable: false),
                    InputName = table.Column<string>(maxLength: 200, nullable: true),
                    InputSize = table.Column<int>(nullable: false),
                    InputHash = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
                    InputContentType = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    ResponseSubscriptionId = table.Column<int>(nullable: true),
                    RetryFor = table.Column<string>(unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Xchanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Xchanges_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartnerApiCredentials",
                columns: table => new
                {
                    PartnerId = table.Column<int>(nullable: false),
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 500, nullable: false),
                    Key = table.Column<string>(unicode: false, maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerApiCredentials", x => new { x.PartnerId, x.Id });
                    table.ForeignKey(
                        name: "FK_PartnerApiCredentials_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    DocumentId = table.Column<int>(nullable: false),
                    Type = table.Column<byte>(nullable: false),
                    PartnerId = table.Column<int>(nullable: true),
                    Temporary = table.Column<bool>(nullable: false),
                    ValidatorId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    HandlerId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    MapperId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    ValidatorProperties = table.Column<string>(nullable: true),
                    HandlerProperties = table.Column<string>(nullable: true),
                    MapperProperties = table.Column<string>(nullable: true),
                    ReceiverProperties = table.Column<string>(nullable: true),
                    DocumentFilter = table.Column<string>(nullable: true),
                    Inactive = table.Column<bool>(nullable: false),
                    ResponseSubscriptionId = table.Column<int>(nullable: true),
                    AggregationForId = table.Column<int>(nullable: true),
                    AggregationTarget = table.Column<byte>(nullable: false),
                    AggregateOn = table.Column<DateTime>(nullable: true),
                    ReceiverId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    ReceiveOn = table.Column<DateTime>(nullable: true),
                    ConsecutiveFailures = table.Column<int>(nullable: false),
                    LastException = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Subscriptions_AggregationForId",
                        column: x => x.AggregationForId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Subscriptions_ResponseSubscriptionId",
                        column: x => x.ResponseSubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "XchangeAggregations",
                columns: table => new
                {
                    Id = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
                    AggregatedOn = table.Column<DateTime>(nullable: false),
                    AggregationXchangeId = table.Column<string>(unicode: false, maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeAggregations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XchangeAggregations_Xchanges_Id",
                        column: x => x.Id,
                        principalTable: "Xchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "XchangeDeliveries",
                columns: table => new
                {
                    Id = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
                    DeliveredOn = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XchangeDeliveries_Xchanges_Id",
                        column: x => x.Id,
                        principalTable: "Xchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "XchangePromotedProperties",
                columns: table => new
                {
                    Id = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
                    Properties = table.Column<string>(nullable: true),
                    Hits = table.Column<string>(unicode: false, maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangePromotedProperties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XchangePromotedProperties_Xchanges_Id",
                        column: x => x.Id,
                        principalTable: "Xchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "XchangeResults",
                columns: table => new
                {
                    Id = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
                    Success = table.Column<bool>(nullable: false),
                    Exception = table.Column<string>(nullable: true),
                    FinishedOn = table.Column<DateTime>(nullable: false),
                    ResponseXchangeId = table.Column<string>(nullable: true),
                    OutputName = table.Column<string>(maxLength: 200, nullable: true),
                    OutputSize = table.Column<int>(nullable: false),
                    OutputHash = table.Column<string>(unicode: false, maxLength: 50, nullable: true),
                    OutputBad = table.Column<bool>(nullable: false),
                    OutputContentType = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    ResponseName = table.Column<string>(maxLength: 200, nullable: true),
                    ResponseSize = table.Column<int>(nullable: false),
                    ResponseHash = table.Column<string>(unicode: false, maxLength: 50, nullable: true),
                    ResponseBad = table.Column<bool>(nullable: false),
                    ResponseContentType = table.Column<string>(unicode: false, maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XchangeResults_Xchanges_Id",
                        column: x => x.Id,
                        principalTable: "Xchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionAggregationSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Recurrence = table.Column<byte>(nullable: false),
                    On = table.Column<double>(nullable: false),
                    Backwards = table.Column<bool>(nullable: false),
                    SubscriptionId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionAggregationSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionAggregationSchedules_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionReceiveSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Recurrence = table.Column<byte>(nullable: false),
                    On = table.Column<double>(nullable: false),
                    Backwards = table.Column<bool>(nullable: false),
                    SubscriptionId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionReceiveSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionReceiveSchedules_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Documents",
                columns: new[] { "Id", "BusEnabled", "BusMessageTypeName", "DuplicateInterval", "Name", "PromotedProperties" },
                values: new object[] { 10001, false, null, 0, "Aggregation Document", "{}" });

            migrationBuilder.InsertData(
                table: "Partners",
                columns: new[] { "Id", "Name" },
                values: new object[] { 1, "SYSTEM" });

            migrationBuilder.InsertData(
                table: "PartnerApiCredentials",
                columns: new[] { "PartnerId", "Id", "Key", "Name" },
                values: new object[] { 1, 1, "7facc758283844b49cc4ffd26a75b1de", "default" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BusMessageTypeName",
                table: "Documents",
                column: "BusMessageTypeName",
                unique: true,
                filter: "[BusMessageTypeName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Name",
                table: "Documents",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartnerApiCredentials_Key",
                table: "PartnerApiCredentials",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAggregationSchedules_SubscriptionId",
                table: "SubscriptionAggregationSchedules",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionReceiveSchedules_SubscriptionId",
                table: "SubscriptionReceiveSchedules",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_AggregationForId",
                table: "Subscriptions",
                column: "AggregationForId",
                unique: true,
                filter: "[AggregationForId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_DocumentId",
                table: "Subscriptions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PartnerId",
                table: "Subscriptions",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ResponseSubscriptionId",
                table: "Subscriptions",
                column: "ResponseSubscriptionId",
                unique: true,
                filter: "[ResponseSubscriptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_XchangeAggregations_AggregationXchangeId",
                table: "XchangeAggregations",
                column: "AggregationXchangeId");

            migrationBuilder.CreateIndex(
                name: "IX_XchangeDeliveries_DeliveredOn",
                table: "XchangeDeliveries",
                column: "DeliveredOn");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_DocumentId",
                table: "Xchanges",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_InputHash",
                table: "Xchanges",
                column: "InputHash");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_RetryFor",
                table: "Xchanges",
                column: "RetryFor");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_StartedOn",
                table: "Xchanges",
                column: "StartedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_SubscriptionId",
                table: "Xchanges",
                column: "SubscriptionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartnerApiCredentials");

            migrationBuilder.DropTable(
                name: "SubscriptionAggregationSchedules");

            migrationBuilder.DropTable(
                name: "SubscriptionReceiveSchedules");

            migrationBuilder.DropTable(
                name: "XchangeAggregations");

            migrationBuilder.DropTable(
                name: "XchangeDeliveries");

            migrationBuilder.DropTable(
                name: "XchangePromotedProperties");

            migrationBuilder.DropTable(
                name: "XchangeResults");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Xchanges");

            migrationBuilder.DropTable(
                name: "Partners");

            migrationBuilder.DropTable(
                name: "Documents");
        }
    }
}
