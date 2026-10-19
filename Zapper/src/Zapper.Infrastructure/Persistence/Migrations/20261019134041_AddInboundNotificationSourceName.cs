using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zapper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundNotificationSourceName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE "InboundNotifications_reordered" (
                    "Id" uuid NOT NULL,
                    "Source" integer NOT NULL,
                    "SourceName" text GENERATED ALWAYS AS (CASE "Source" WHEN 0 THEN 'PaymentAdvice' WHEN 1 THEN 'TransactionStatus' ELSE 'Unknown' END) STORED,
                    "DeduplicationKey" character varying(200) NOT NULL,
                    "ExternalEventId" character varying(200),
                    "AcmeTransactionId" character varying(50),
                    "RetrievalReferenceNumber" character varying(12),
                    "ReceivedAt" timestamp with time zone NOT NULL,
                    "ProviderOccurredAt" timestamp with time zone,
                    "PayloadSha256" character varying(64) NOT NULL,
                    "ProcessingStatus" integer NOT NULL,
                    "ProcessingNote" character varying(1000),
                    "RawPayload" text NOT NULL,
                    CONSTRAINT "PK_InboundNotifications_reordered" PRIMARY KEY ("Id")
                );

                INSERT INTO "InboundNotifications_reordered" (
                    "Id", "Source", "DeduplicationKey", "ExternalEventId", "AcmeTransactionId",
                    "RetrievalReferenceNumber", "ReceivedAt", "ProviderOccurredAt", "PayloadSha256",
                    "ProcessingStatus", "ProcessingNote", "RawPayload")
                SELECT
                    "Id", "Source", "DeduplicationKey", "ExternalEventId", "AcmeTransactionId",
                    "RetrievalReferenceNumber", "ReceivedAt", "ProviderOccurredAt", "PayloadSha256",
                    "ProcessingStatus", "ProcessingNote", "RawPayload"
                FROM "InboundNotifications";

                DROP TABLE "InboundNotifications";
                ALTER TABLE "InboundNotifications_reordered" RENAME TO "InboundNotifications";
                ALTER TABLE "InboundNotifications" RENAME CONSTRAINT "PK_InboundNotifications_reordered" TO "PK_InboundNotifications";

                CREATE INDEX "IX_InboundNotifications_AcmeTransactionId" ON "InboundNotifications" ("AcmeTransactionId");
                CREATE INDEX "IX_InboundNotifications_ReceivedAt" ON "InboundNotifications" ("ReceivedAt");
                CREATE INDEX "IX_InboundNotifications_Source_DeduplicationKey" ON "InboundNotifications" ("Source", "DeduplicationKey");
                CREATE INDEX "IX_InboundNotifications_Source_ExternalEventId" ON "InboundNotifications" ("Source", "ExternalEventId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE "InboundNotifications_reordered" (
                    "Id" uuid NOT NULL,
                    "Source" integer NOT NULL,
                    "DeduplicationKey" character varying(200) NOT NULL,
                    "ExternalEventId" character varying(200),
                    "AcmeTransactionId" character varying(50),
                    "RetrievalReferenceNumber" character varying(12),
                    "ReceivedAt" timestamp with time zone NOT NULL,
                    "ProviderOccurredAt" timestamp with time zone,
                    "PayloadSha256" character varying(64) NOT NULL,
                    "ProcessingStatus" integer NOT NULL,
                    "ProcessingNote" character varying(1000),
                    "RawPayload" text NOT NULL,
                    CONSTRAINT "PK_InboundNotifications_reordered" PRIMARY KEY ("Id")
                );

                INSERT INTO "InboundNotifications_reordered" (
                    "Id", "Source", "DeduplicationKey", "ExternalEventId", "AcmeTransactionId",
                    "RetrievalReferenceNumber", "ReceivedAt", "ProviderOccurredAt", "PayloadSha256",
                    "ProcessingStatus", "ProcessingNote", "RawPayload")
                SELECT
                    "Id", "Source", "DeduplicationKey", "ExternalEventId", "AcmeTransactionId",
                    "RetrievalReferenceNumber", "ReceivedAt", "ProviderOccurredAt", "PayloadSha256",
                    "ProcessingStatus", "ProcessingNote", "RawPayload"
                FROM "InboundNotifications";

                DROP TABLE "InboundNotifications";
                ALTER TABLE "InboundNotifications_reordered" RENAME TO "InboundNotifications";
                ALTER TABLE "InboundNotifications" RENAME CONSTRAINT "PK_InboundNotifications_reordered" TO "PK_InboundNotifications";

                CREATE INDEX "IX_InboundNotifications_AcmeTransactionId" ON "InboundNotifications" ("AcmeTransactionId");
                CREATE INDEX "IX_InboundNotifications_ReceivedAt" ON "InboundNotifications" ("ReceivedAt");
                CREATE INDEX "IX_InboundNotifications_Source_DeduplicationKey" ON "InboundNotifications" ("Source", "DeduplicationKey");
                CREATE INDEX "IX_InboundNotifications_Source_ExternalEventId" ON "InboundNotifications" ("Source", "ExternalEventId");
                """);
        }
    }
}
