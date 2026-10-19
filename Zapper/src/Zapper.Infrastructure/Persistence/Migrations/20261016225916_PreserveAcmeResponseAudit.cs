using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zapper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc/>
    public partial class PreserveAcmeResponseAudit : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcmeAuthCode",
                table: "Payments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcmeClientMessage",
                table: "Payments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcmeOtherTransactionId",
                table: "Payments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcmeReconId",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcmeResponseDescription",
                table: "Payments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcmeResponseMessageId",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcmeResponsePayload",
                table: "Payments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcmeResponseSource",
                table: "Payments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcmeTransactionStatus",
                table: "Payments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcmeAuthCode",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AcmeClientMessage",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AcmeOtherTransactionId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AcmeReconId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AcmeResponseDescription",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AcmeResponseMessageId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AcmeResponsePayload",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AcmeResponseSource",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AcmeTransactionStatus",
                table: "Payments");
        }
    }
}