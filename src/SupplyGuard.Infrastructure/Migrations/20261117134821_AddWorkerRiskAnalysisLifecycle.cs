using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerRiskAnalysisLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pending",
                schema: "supplyguard",
                table: "outbox_messages");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAtUtc",
                schema: "supplyguard",
                table: "xai_audit_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExplanationText",
                schema: "supplyguard",
                table: "xai_audit_logs",
                type: "character varying(100000)",
                maxLength: 100000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestMessageId",
                schema: "supplyguard",
                table: "xai_audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RequestedAtUtc",
                schema: "supplyguard",
                table: "xai_audit_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QuarantinedAtUtc",
                schema: "supplyguard",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "supplyguard",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredAtUtc", "Id" },
                filter: "\"ProcessedAtUtc\" IS NULL AND \"QuarantinedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pending",
                schema: "supplyguard",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                schema: "supplyguard",
                table: "xai_audit_logs");

            migrationBuilder.DropColumn(
                name: "ExplanationText",
                schema: "supplyguard",
                table: "xai_audit_logs");

            migrationBuilder.DropColumn(
                name: "RequestMessageId",
                schema: "supplyguard",
                table: "xai_audit_logs");

            migrationBuilder.DropColumn(
                name: "RequestedAtUtc",
                schema: "supplyguard",
                table: "xai_audit_logs");

            migrationBuilder.DropColumn(
                name: "QuarantinedAtUtc",
                schema: "supplyguard",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "supplyguard",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredAtUtc", "Id" },
                filter: "\"ProcessedAtUtc\" IS NULL");
        }
    }
}
