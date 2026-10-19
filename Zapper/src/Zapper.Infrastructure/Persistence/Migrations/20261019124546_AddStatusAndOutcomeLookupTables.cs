using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zapper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusAndOutcomeLookupTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentLifecycleEventTypes",
                columns: table => new
                {
                    EventTypeCode = table.Column<int>(type: "integer", nullable: false),
                    EventTypeName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentLifecycleEventTypes", x => x.EventTypeCode);
                });

            migrationBuilder.CreateTable(
                name: "PaymentStatuses",
                columns: table => new
                {
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    StatusName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentStatuses", x => x.StatusCode);
                });

            migrationBuilder.CreateTable(
                name: "PaymentOutcomeCodeMappings",
                columns: table => new
                {
                    ResponseCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TransactionStatusContext = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Meaning = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    PaymentStatusCode = table.Column<int>(type: "integer", nullable: true),
                    EventTypeCode = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentOutcomeCodeMappings", x => new { x.ResponseCode, x.TransactionStatusContext });
                    table.ForeignKey(
                        name: "FK_PaymentOutcomeCodeMappings_PaymentLifecycleEventTypes_Event~",
                        column: x => x.EventTypeCode,
                        principalTable: "PaymentLifecycleEventTypes",
                        principalColumn: "EventTypeCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentOutcomeCodeMappings_PaymentStatuses_PaymentStatusCode",
                        column: x => x.PaymentStatusCode,
                        principalTable: "PaymentStatuses",
                        principalColumn: "StatusCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PaymentLifecycleEventTypes",
                columns: new[] { "EventTypeCode", "Description", "EventTypeName" },
                values: new object[,]
                {
                    { 0, "Zapper recorded the payment submission.", "Submitted" },
                    { 1, "The provider reports that processing is underway.", "Processing" },
                    { 2, "Zapper could not confirm the payment outcome.", "OutcomeUnknown" },
                    { 3, "An approval outcome was recorded.", "Approved" },
                    { 4, "A decline outcome was recorded.", "Declined" },
                    { 5, "A payment submission failure was recorded.", "SubmissionFailed" },
                    { 6, "A payment reversal was recorded.", "Reversed" },
                    { 7, "A refund event was recorded.", "RefundApplied" }
                });

            migrationBuilder.InsertData(
                table: "PaymentStatuses",
                columns: new[] { "StatusCode", "Description", "StatusName" },
                values: new object[,]
                {
                    { 0, "Payment has been recorded and is awaiting an outcome.", "Pending" },
                    { 1, "The provider reports that payment processing is still underway.", "Processing" },
                    { 2, "The provider outcome could not be confirmed.", "OutcomeUnknown" },
                    { 3, "Payment was approved.", "Approved" },
                    { 4, "Payment was declined.", "Declined" },
                    { 5, "The payment submission failed or was rejected.", "SubmissionFailed" },
                    { 6, "The payment was reversed.", "Reversed" },
                    { 7, "Part of the authorized amount was refunded.", "PartiallyRefunded" },
                    { 8, "The payment was fully refunded.", "Refunded" }
                });

            migrationBuilder.InsertData(
                table: "PaymentOutcomeCodeMappings",
                columns: new[] { "ResponseCode", "TransactionStatusContext", "EventTypeCode", "Meaning", "Origin", "PaymentStatusCode" },
                values: new object[,]
                {
                    { "00", "Complete", 3, "Approved EFT collection", "AcmeStub", 3 },
                    { "00", "Refunded", 7, "Payment refunded", "AcmeStub", 8 },
                    { "00", "Reversed", 6, "Payment reversed", "AcmeStub", 6 },
                    { "00", "Success", 3, "Approved", "AcmeStub", 3 },
                    { "05", "Success", 4, "Do not honour", "AcmeStub", 4 },
                    { "51", "Success", 4, "Insufficient funds", "AcmeStub", 4 },
                    { "54", "Success", 4, "Expired card", "AcmeStub", 4 },
                    { "91", "Success", 4, "Issuer or switch unavailable", "AcmeStub", 4 },
                    { "COMPL", "Complete", 3, "EFT collection completed", "AcmeStub", 3 },
                    { "E01", "Failure", 5, "Invalid request", "AcmeStub", 5 },
                    { "E02", "Failure", 5, "Unauthorized request", "AcmeStub", 5 },
                    { "E09", "Failure", 5, "Duplicate transaction or retrieval reference conflict", "AcmeStub", 5 },
                    { "E29", "Failure", 5, "Gateway request limit exceeded", "AcmeStub", 5 },
                    { "E50", "Failure", 5, "Gateway temporarily unavailable", "AcmeStub", 5 },
                    { "PEND", "Processing", 1, "Transaction is pending", "AcmeStub", 1 },
                    { "TIMEOUT", "(none)", 2, "Provider outcome is unknown after a timeout", "Zapper", 2 },
                    { "TRANSPORT_UNKNOWN", "(none)", 2, "Provider outcome is unknown after a transport failure", "Zapper", 2 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status",
                table: "Payments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLifecycleEvents_Type",
                table: "PaymentLifecycleEvents",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOutcomeCodeMappings_EventTypeCode",
                table: "PaymentOutcomeCodeMappings",
                column: "EventTypeCode");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOutcomeCodeMappings_PaymentStatusCode",
                table: "PaymentOutcomeCodeMappings",
                column: "PaymentStatusCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentLifecycleEvents_PaymentLifecycleEventTypes_Type",
                table: "PaymentLifecycleEvents",
                column: "Type",
                principalTable: "PaymentLifecycleEventTypes",
                principalColumn: "EventTypeCode",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_PaymentStatuses_Status",
                table: "Payments",
                column: "Status",
                principalTable: "PaymentStatuses",
                principalColumn: "StatusCode",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentLifecycleEvents_PaymentLifecycleEventTypes_Type",
                table: "PaymentLifecycleEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_PaymentStatuses_Status",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "PaymentOutcomeCodeMappings");

            migrationBuilder.DropTable(
                name: "PaymentLifecycleEventTypes");

            migrationBuilder.DropTable(
                name: "PaymentStatuses");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Status",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_PaymentLifecycleEvents_Type",
                table: "PaymentLifecycleEvents");
        }
    }
}
