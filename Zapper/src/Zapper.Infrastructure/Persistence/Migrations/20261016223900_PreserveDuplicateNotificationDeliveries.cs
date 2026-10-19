using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zapper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc/>
    public partial class PreserveDuplicateNotificationDeliveries : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InboundNotifications_Source_DeduplicationKey",
                table: "InboundNotifications");

            migrationBuilder.DropIndex(
                name: "IX_InboundNotifications_Source_ExternalEventId",
                table: "InboundNotifications");

            migrationBuilder.CreateIndex(
                name: "IX_InboundNotifications_Source_DeduplicationKey",
                table: "InboundNotifications",
                columns: new[] { "Source", "DeduplicationKey" });

            migrationBuilder.CreateIndex(
                name: "IX_InboundNotifications_Source_ExternalEventId",
                table: "InboundNotifications",
                columns: new[] { "Source", "ExternalEventId" });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InboundNotifications_Source_DeduplicationKey",
                table: "InboundNotifications");

            migrationBuilder.DropIndex(
                name: "IX_InboundNotifications_Source_ExternalEventId",
                table: "InboundNotifications");

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
        }
    }
}