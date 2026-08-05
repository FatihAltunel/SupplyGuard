using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "supplyguard");

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "supplyguard",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TaxNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    WebsiteUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Industry = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SupplierCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsCriticalSupplier = table.Column<bool>(type: "boolean", nullable: false),
                    OnboardingDateUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRiskAssessmentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "risk_assessments",
                schema: "supplyguard",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverallRiskScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    OverallRiskLevel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    AssessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Rationale = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_risk_assessments_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplyguard",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "risk_indicators",
                schema: "supplyguard",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IndicatorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Severity = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    RawValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    NormalizedScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_indicators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_risk_indicators_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplyguard",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "early_warnings",
                schema: "supplyguard",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    RiskAssessmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_early_warnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_early_warnings_risk_assessments_RiskAssessmentId",
                        column: x => x.RiskAssessmentId,
                        principalSchema: "supplyguard",
                        principalTable: "risk_assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_early_warnings_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplyguard",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "risk_scores",
                schema: "supplyguard",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RiskAssessmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_scores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_risk_scores_risk_assessments_RiskAssessmentId",
                        column: x => x.RiskAssessmentId,
                        principalSchema: "supplyguard",
                        principalTable: "risk_assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "xai_audit_logs",
                schema: "supplyguard",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    RiskAssessmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestPayload = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    ResponsePayload = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: true),
                    ConfidenceScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    IsSuccessful = table.Column<bool>(type: "boolean", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExecutedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_xai_audit_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_xai_audit_logs_risk_assessments_RiskAssessmentId",
                        column: x => x.RiskAssessmentId,
                        principalSchema: "supplyguard",
                        principalTable: "risk_assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_xai_audit_logs_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplyguard",
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_early_warnings_RiskAssessmentId",
                schema: "supplyguard",
                table: "early_warnings",
                column: "RiskAssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_early_warnings_SupplierId_Status_DetectedAtUtc",
                schema: "supplyguard",
                table: "early_warnings",
                columns: new[] { "SupplierId", "Status", "DetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_assessments_SupplierId_AssessedAtUtc",
                schema: "supplyguard",
                table: "risk_assessments",
                columns: new[] { "SupplierId", "AssessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_indicators_SupplierId_Category_IsActive",
                schema: "supplyguard",
                table: "risk_indicators",
                columns: new[] { "SupplierId", "Category", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_scores_RiskAssessmentId_Category",
                schema: "supplyguard",
                table: "risk_scores",
                columns: new[] { "RiskAssessmentId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Suppliers_CountryCode_TaxNumber",
                schema: "supplyguard",
                table: "suppliers",
                columns: new[] { "CountryCode", "TaxNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_xai_audit_logs_CorrelationId",
                schema: "supplyguard",
                table: "xai_audit_logs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_xai_audit_logs_RiskAssessmentId",
                schema: "supplyguard",
                table: "xai_audit_logs",
                column: "RiskAssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_xai_audit_logs_SupplierId_ExecutedAtUtc",
                schema: "supplyguard",
                table: "xai_audit_logs",
                columns: new[] { "SupplierId", "ExecutedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "early_warnings",
                schema: "supplyguard");

            migrationBuilder.DropTable(
                name: "risk_indicators",
                schema: "supplyguard");

            migrationBuilder.DropTable(
                name: "risk_scores",
                schema: "supplyguard");

            migrationBuilder.DropTable(
                name: "xai_audit_logs",
                schema: "supplyguard");

            migrationBuilder.DropTable(
                name: "risk_assessments",
                schema: "supplyguard");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "supplyguard");
        }
    }
}
