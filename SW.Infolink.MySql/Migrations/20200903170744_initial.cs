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
                    InputFileName = table.Column<string>(maxLength: 200, nullable: true),
                    InputFileSize = table.Column<int>(nullable: false),
                    InputFileHash = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
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
                    HandlerId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    MapperId = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    Temporary = table.Column<bool>(nullable: false),
                    Aggregate = table.Column<bool>(nullable: false),
                    HandlerProperties = table.Column<string>(nullable: true),
                    MapperProperties = table.Column<string>(nullable: true),
                    DocumentFilter = table.Column<string>(nullable: true),
                    Inactive = table.Column<bool>(nullable: false),
                    ResponseSubscriptionId = table.Column<int>(nullable: true),
                    PartnerId = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscribers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscribers_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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
                    XchangeId = table.Column<string>(unicode: false, maxLength: 50, nullable: true),
                    DeliveredOn = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XchangeDeliveries_Xchanges_XchangeId",
                        column: x => x.XchangeId,
                        principalTable: "Xchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "XchangeResults",
                columns: table => new
                {
                    Id = table.Column<string>(unicode: false, maxLength: 50, nullable: false),
                    XchangeId = table.Column<string>(unicode: false, maxLength: 50, nullable: true),
                    Success = table.Column<bool>(nullable: false),
                    Exception = table.Column<string>(nullable: true),
                    FinishedOn = table.Column<DateTime>(nullable: true),
                    ResponseXchangeId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XchangeResults_Xchanges_XchangeId",
                        column: x => x.XchangeId,
                        principalTable: "Xchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Receivers",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    ReceiverId = table.Column<string>(unicode: false, maxLength: 200, nullable: false),
                    Properties = table.Column<string>(nullable: true),
                    ReceiveOn = table.Column<DateTime>(nullable: true),
                    SubscriberId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Receivers_Subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriberSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Recurrence = table.Column<byte>(nullable: false),
                    On = table.Column<TimeSpan>(nullable: false),
                    Backwards = table.Column<bool>(nullable: false),
                    SubscriptionId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriberSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriberSchedules_Subscribers_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReceiverSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
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
                        principalTable: "Receivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_Receivers_SubscriberId",
                table: "Receivers",
                column: "SubscriberId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiverSchedules_ReceiverId",
                table: "ReceiverSchedules",
                column: "ReceiverId");

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
                name: "IX_SubscriberSchedules_SubscriptionId",
                table: "SubscriberSchedules",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_XchangeDeliveries_DeliveredOn",
                table: "XchangeDeliveries",
                column: "DeliveredOn");

            migrationBuilder.CreateIndex(
                name: "IX_XchangeDeliveries_XchangeId",
                table: "XchangeDeliveries",
                column: "XchangeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XchangeResults_XchangeId",
                table: "XchangeResults",
                column: "XchangeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_DeliverOn",
                table: "Xchanges",
                column: "DeliverOn");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_InputFileHash",
                table: "Xchanges",
                column: "InputFileHash");

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_SubscriptionId",
                table: "Xchanges",
                column: "SubscriptionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "PartnerApiCredentials");

            migrationBuilder.DropTable(
                name: "ReceiverSchedules");

            migrationBuilder.DropTable(
                name: "SubscriberSchedules");

            migrationBuilder.DropTable(
                name: "XchangeDeliveries");

            migrationBuilder.DropTable(
                name: "XchangeResults");

            migrationBuilder.DropTable(
                name: "Receivers");

            migrationBuilder.DropTable(
                name: "Xchanges");

            migrationBuilder.DropTable(
                name: "Subscribers");

            migrationBuilder.DropTable(
                name: "Partners");
        }
    }
}
