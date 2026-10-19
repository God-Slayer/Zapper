using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zapper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc/>
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboundNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExternalEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AcmeTransactionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RetrievalReferenceNumber = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProviderOccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayloadSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    ProcessingNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppPaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubMerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AcmeTransactionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AcmeOrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RetrievalReferenceNumber = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    AuthorizedAmountMinorUnits = table.Column<long>(type: "bigint", nullable: true),
                    AcmeResponseCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastPaymentOutcomeAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubMerchants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ZapperMerchantId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TradingName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RegionCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    MerchantCategoryCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubMerchants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoredCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcmeToken = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredCards_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentLifecycleEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResponseCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentLifecycleEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentLifecycleEvents_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_EmailAddress",
                table: "Customers",
                column: "EmailAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundNotifications_AcmeTransactionId",
                table: "InboundNotifications",
                column: "AcmeTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_InboundNotifications_ReceivedAt",
                table: "InboundNotifications",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InboundNotifications_Source_DeduplicationKey",
                table: "InboundNotifications",
                columns: new[] { "Source", "DeduplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundNotifications_Source_ExternalEventId",
                table: "InboundNotifications",
                columns: new[] { "Source", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLifecycleEvents_OccurredAt_Type",
                table: "PaymentLifecycleEvents",
                columns: new[] { "OccurredAt", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLifecycleEvents_PaymentId_ExternalEventId",
                table: "PaymentLifecycleEvents",
                columns: new[] { "PaymentId", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_AcmeTransactionId",
                table: "Payments",
                column: "AcmeTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_AppPaymentId",
                table: "Payments",
                column: "AppPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CustomerId_CreatedAt",
                table: "Payments",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_RetrievalReferenceNumber",
                table: "Payments",
                column: "RetrievalReferenceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SubMerchantId_CreatedAt",
                table: "Payments",
                columns: new[] { "SubMerchantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredCards_AcmeToken",
                table: "StoredCards",
                column: "AcmeToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredCards_CustomerId",
                table: "StoredCards",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SubMerchants_ZapperMerchantId",
                table: "SubMerchants",
                column: "ZapperMerchantId",
                unique: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundNotifications");

            migrationBuilder.DropTable(
                name: "PaymentLifecycleEvents");

            migrationBuilder.DropTable(
                name: "StoredCards");

            migrationBuilder.DropTable(
                name: "SubMerchants");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}