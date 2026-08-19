using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateXAIAuditLogStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExplanationStatus",
                schema: "supplyguard",
                table: "xai_audit_logs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE supplyguard.xai_audit_logs
                SET "ExplanationStatus" = CASE
                    WHEN "IsSuccessful" THEN 'Completed'
                    ELSE 'Failed'
                END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ExplanationStatus",
                schema: "supplyguard",
                table: "xai_audit_logs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExplanationStatus",
                schema: "supplyguard",
                table: "xai_audit_logs");
        }
    }
}
