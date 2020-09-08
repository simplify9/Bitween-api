using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class initial : Migration
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
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
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
                    DeliverOn = table.Column<DateTime>(nullable: true),
                    ResponseSubscriptionId = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Xchanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PartnerApiCredentials",
                columns: table => new
                {
                    PartnerId = table.Column<int>(nullable: false),
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
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
                name: "Subscribers",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    DocumentId = table.Column<int>(nullable: false),
                    Type = table.Column<byte>(nullable: false),
                    PartnerId = table.Column<int>(nullable: true),
                    Temporary = table.Column<bool>(nullable: false),
                    ValidatorId = table.Column<string>(nullable: true),
                    HandlerId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    MapperId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    Aggregate = table.Column<bool>(nullable: false),
                    ValidatorProperties = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    HandlerProperties = table.Column<string>(nullable: true),
                    MapperProperties = table.Column<string>(nullable: true),
                    ReceiverProperties = table.Column<string>(nullable: true),
                    DocumentFilter = table.Column<string>(nullable: true),
                    Inactive = table.Column<bool>(nullable: false),
                    ResponseSubscriptionId = table.Column<int>(nullable: true),
                    ReceiverId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    ReceiveOn = table.Column<DateTime>(nullable: true),
                    ReceiveConsecutiveFailures = table.Column<int>(nullable: false),
                    ReceiveLastException = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscribers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscribers_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscribers_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscribers_Subscribers_ResponseSubscriptionId",
                        column: x => x.ResponseSubscriptionId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                    Properties = table.Column<string>(nullable: true)
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
                    ResponseName = table.Column<string>(maxLength: 200, nullable: true),
                    ResponseSize = table.Column<int>(nullable: false),
                    ResponseHash = table.Column<string>(unicode: false, maxLength: 50, nullable: true)
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
                name: "SubscriptionReceiveSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Recurrence = table.Column<byte>(nullable: false),
                    On = table.Column<double>(nullable: false),
                    Backwards = table.Column<bool>(nullable: false),
                    SubscriptionId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionReceiveSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionReceiveSchedules_Subscribers_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Recurrence = table.Column<byte>(nullable: false),
                    On = table.Column<double>(nullable: false),
                    Backwards = table.Column<bool>(nullable: false),
                    SubscriptionId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionSchedules_Subscribers_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                unique: true);

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
                name: "IX_Subscribers_DocumentId",
                table: "Subscribers",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscribers_PartnerId",
                table: "Subscribers",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscribers_ResponseSubscriptionId",
                table: "Subscribers",
                column: "ResponseSubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionReceiveSchedules_SubscriptionId",
                table: "SubscriptionReceiveSchedules",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSchedules_SubscriptionId",
                table: "SubscriptionSchedules",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_XchangeDeliveries_DeliveredOn",
                table: "XchangeDeliveries",
                column: "DeliveredOn");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_DeliverOn",
                table: "Xchanges",
                column: "DeliverOn");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_InputHash",
                table: "Xchanges",
                column: "InputHash");

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
                name: "SubscriptionReceiveSchedules");

            migrationBuilder.DropTable(
                name: "SubscriptionSchedules");

            migrationBuilder.DropTable(
                name: "XchangeDeliveries");

            migrationBuilder.DropTable(
                name: "XchangePromotedProperties");

            migrationBuilder.DropTable(
                name: "XchangeResults");

            migrationBuilder.DropTable(
                name: "Subscribers");

            migrationBuilder.DropTable(
                name: "Xchanges");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Partners");
        }
    }
}
