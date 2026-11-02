using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceRiskAnalysisIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve append-only history: require duplicates to be reviewed explicitly.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM supplyguard.xai_audit_logs
                        GROUP BY "CorrelationId" HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Duplicate XAI audit correlations exist; review history before applying uniqueness.';
                    END IF;
                END; $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_xai_audit_logs_CorrelationId",
                schema: "supplyguard",
                table: "xai_audit_logs");

            migrationBuilder.CreateIndex(
                name: "IX_xai_audit_logs_CorrelationId",
                schema: "supplyguard",
                table: "xai_audit_logs",
                column: "CorrelationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_xai_audit_logs_CorrelationId",
                schema: "supplyguard",
                table: "xai_audit_logs");

            migrationBuilder.CreateIndex(
                name: "IX_xai_audit_logs_CorrelationId",
                schema: "supplyguard",
                table: "xai_audit_logs",
                column: "CorrelationId");
        }
    }
}
