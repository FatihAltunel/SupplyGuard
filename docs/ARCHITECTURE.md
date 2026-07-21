# SupplyGuard Architecture

## Current State (End of Sprint 1)

The `SupplyGuard.Domain` layer is implemented using Clean Architecture and strict Domain-Driven Design principles. It contains no dependencies on EF Core, PostgreSQL, ASP.NET Core, messaging, caching, or XAI provider SDKs. The Domain layer is the source of truth for supplier-risk business rules; outer layers will persist, expose, and orchestrate it in later sprints.

## Domain Models

### Base classes

- `BaseEntity`
  - `Guid Id` with a private setter.
  - Derived entities create a non-empty identifier through the protected constructor; EF Core can materialize entities through the protected parameterless constructor.

- `AuditableEntity : BaseEntity`
  - `Guid? CreatedByUserId`
  - `DateTimeOffset CreatedAtUtc`
  - `Guid? LastModifiedByUserId`
  - `DateTimeOffset? LastModifiedAtUtc`
  - Audit fields use nullable user identifiers because Identity is scheduled for Sprint 3. Domain mutation methods call the protected audit-touch behavior to record modification metadata.

### Aggregate root

- `Supplier : AuditableEntity`
  - Holds the supplier business identity: `Name`, `TaxNumber`, and ISO alpha-2 `CountryCode`.
  - The business key is the combination of `CountryCode + TaxNumber`; neither is exposed for unrestricted mutation.
  - Manages profile, contact, supplier status, criticality, onboarding date, and latest risk-assessment date.
  - Owns read-only collections of `RiskAssessment`, `RiskIndicator`, `EarlyWarning`, and `XAIAuditLog` records.
  - Exposes aggregate behavior such as changing status, updating contact/profile details, setting criticality, and adding related risk records only when their `SupplierId` matches the aggregate.

### Entities

- `RiskIndicator : AuditableEntity`
  - Records an observed risk signal, including category, stable indicator code, source, raw value, normalized score, weight, severity, observation/expiry timestamps, and active state.

- `RiskAssessment : AuditableEntity`
  - Represents a supplier evaluation at a point in time, including overall score, overall risk level, rationale, outcome, and a read-only collection of `RiskScore` records.
  - Ensures only one score per `RiskCategory` and recalculates the weighted overall score when a score is added.

- `RiskScore : BaseEntity`
  - Captures one category-level score within a risk assessment: category, score, weight, risk level, explanation, and calculation timestamp.

- `EarlyWarning : AuditableEntity`
  - Captures a warning raised for a supplier, optionally linked to a risk assessment.
  - Enforces controlled transitions for acknowledgement, resolution, and dismissal, including actor and timestamp data.

- `XAIAuditLog : BaseEntity`
  - Immutable, append-only trace of an XAI model invocation.
  - Records supplier/assessment linkage, model name and version, correlation ID, request/response payloads, confidence score, latency, execution result, failure details, and execution time.
  - Intentionally does not inherit from `AuditableEntity`; audit records must never be updated after creation.

### Enums

- `SupplierStatus`: `Active`, `Suspended`, `Inactive`, `Blocked`
- `RiskCategory`: `Financial`, `Operational`, `Compliance`, `Geographic`, `EnvironmentalSocialGovernance`, `Delivery`, `Quality`
- `RiskLevel`: `Low`, `Medium`, `High`, `Critical`
- `WarningSeverity`: `Informational`, `Low`, `Medium`, `High`, `Critical`
- `WarningStatus`: `Open`, `Acknowledged`, `Resolved`, `Dismissed`

## DDD & Structural Rules Applied

- **No anemic models:** Persisted state uses private setters. Public behavior is expressed through intention-revealing methods rather than unrestricted property mutation.
- **Constructor-enforced invariants:** Public parameterized constructors validate mandatory values, non-empty identifiers, score ranges, weight ranges, timestamp ordering, and model-execution outcomes before an entity enters a valid state.
- **EF Core compatibility:** Every entity has a private parameterless constructor for materialization. This preserves encapsulation while remaining Code-First compatible.
- **Aggregate boundaries:** `Supplier` validates that related risk records belong to its own identifier before adding them. Its child collections are exposed as read-only.
- **Audit discipline:** Auditable entities use UTC `DateTimeOffset` values and nullable actor IDs. `XAIAuditLog` is append-only, has no state-mutation methods, and inherits directly from `BaseEntity`.
- **Risk calculation:** Normalized risk scores use the `0..100` range, indicator/score weights use the `(0..1]` range, and an assessment recalculates its weighted overall score from its category scores.

## Pending Infrastructure Rules (For Sprint 2)

The following responsibilities are deliberately deferred to the appropriate outer layers:

- **Composite supplier business-key index:** Configure a unique PostgreSQL/EF Core index for `Supplier.CountryCode + Supplier.TaxNumber` in Infrastructure Fluent API configuration.
- **PostgreSQL mappings:** Configure field lengths, required fields, relationships, backing collections, decimal precision, timestamp mappings, and enum conversions in EF Core Fluent API. The Domain project remains persistence-agnostic.
- **XAI payload protection:** Redact or mask sensitive business/PII data in the Application layer before creating an `XAIAuditLog`. Infrastructure will persist only the protected payloads and must avoid logging raw payload content.
- **Identity integration:** Populate `CreatedByUserId` and modification actor fields once ASP.NET Core Identity is introduced in Sprint 3.

