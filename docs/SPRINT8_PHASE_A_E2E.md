# Sprint 8 Phase A E2E

The API and worker communicate only through RabbitMQ. No trained model is involved.

## Prepare the database

Use a local development database and the existing secret/environment configuration.
The migration is generated but is not applied automatically at API startup.

```powershell
dotnet ef database update --project src/SupplyGuard.Infrastructure --startup-project src/SupplyGuard.WebAPI
```

EF's design-time factory requires `SUPPLYGUARD_CONNECTION_STRING` or
`ConnectionStrings__SupplyGuardDatabase` in the invoking shell.

`EnforceRiskAnalysisIdempotency` makes `IX_xai_audit_logs_CorrelationId` unique.
It stops if existing duplicate correlations are present; it never deletes or rewrites
append-only audit history. Inspect duplicates before applying it if necessary:

```sql
SELECT "CorrelationId", COUNT(*)
FROM supplyguard.xai_audit_logs
GROUP BY "CorrelationId"
HAVING COUNT(*) > 1;
```

## Start the services

Start the existing Compose services with your ignored `.env` credentials:

```powershell
docker compose up -d postgres redis rabbitmq
```

Configure/start the API as usual with its PostgreSQL, JWT, Redis, and RabbitMQ secrets.
For a worker running locally, the default broker host is `localhost`, port `5672`,
user/vhost `supplyguard`. Set `RABBITMQ_PASSWORD` to the existing broker password.
For a worker on the Compose network, set `RABBITMQ_HOST=rabbitmq` instead.

```powershell
cd supplyguard-ml-worker
python -m pip install -r requirements.txt
python -m uvicorn main:app --port 8000
```

The worker's `/health` must return 200. It intentionally waits for its initial broker
connection before accepting HTTP traffic.

## Request an analysis

Use an existing supplier ID and an authenticated token with `risk-assessments.create`.
Send the following body to `POST /api/suppliers/{id}/analyze`:

```json
{
  "featureSchemaVersion": 1,
  "observedAtUtc": "2026-11-02T09:00:00.000Z",
  "features": {
    "averageDeliveryDelayDays": 8.0,
    "onTimeDeliveryRate": 0.72,
    "missing": null
  }
}
```

Expected response: `202 Accepted` with `supplierId`, `correlationId`, and
`requestMessageId`. This means durable acceptance. If RabbitMQ is unavailable,
the outbox retains the request and retries with the same IDs.

After the two-second simulation plus delivery time, inspect the correlation:

```sql
SELECT "CorrelationId", "RiskAssessmentId", "ExplanationStatus", "IsSuccessful",
       "ModelName", "ModelVersion", "ConfidenceScore", "FailureCode"
FROM supplyguard.xai_audit_logs
WHERE "CorrelationId" = '<correlationId returned by the API>';
```

Expect one completed audit, one assessment with score `65.00` and level `High`,
confidence `0.9000`, and the dummy explanation. The retained outbox request and
stored response use the v1 JSON contract, including `isMock: true` in the response.

## Resilience checks

- Stop RabbitMQ, submit an analysis, then restart RabbitMQ. The API should durably
  accept the request and the outbox should publish it after its retry delay.
- Stop Redis, submit another analysis, and verify the completion still commits.
  Cache invalidation failure logs a warning; cached data can remain stale until TTL.
- Republish a completed message with its original AMQP metadata and JSON. Expect an
  ACK without a second assessment/audit. The database unique constraint is the final
  cross-process guard; the worker's cache is process-local and bounded.
- In a controlled worker test, force processing to fail through its retries. Expect
  one failed audit, zero confidence, no assessment link, and no new assessment.
  There is intentionally no public HTTP endpoint that forces worker failure.
- Malformed completions go to `risk-analysis.completed.error.queue` only after a
  confirmed quarantine copy. Transient database or settlement failures cause session
  restart and redelivery, with a fresh DI scope each time.

Request/completion queues are `risk-analysis.requested.queue` and
`risk-analysis.completed.queue`, bound to `supplyguard.events`. Keep request queue
DLX settings identical across .NET and Python. Do not purge retained request outbox
rows before their terminal result has been audited.

## Automated checks

```powershell
dotnet test SupplyGuard.sln --no-restore
```

The default suite uses broker doubles and SQLite for persistence workflows.
PostgreSQL row-lock behavior and the full Python pipeline require the live services.
Existing notification broker tests are opt-in via their `RabbitMqLiveFact` settings.
