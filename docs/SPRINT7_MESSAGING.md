# Sprint 7: RabbitMQ notification pipeline

The API stages early-warning messages in the transactional outbox. `AddRabbitMqMessaging` registers the raw RabbitMQ.Client publisher as a singleton and activates both `OutboxProcessor` and `EarlyWarningNotificationConsumer`. Registration and handler execution remain explicit, with no MediatR or MassTransit.

## Local configuration

1. Add `RABBITMQ_PASSWORD` to the existing ignored `.env` file. Optional Compose settings are `RABBITMQ_USER` and `RABBITMQ_VHOST`, both defaulting to `supplyguard`.
2. Set `RabbitMq:Password` through the WebAPI's .NET User Secrets, or set `RabbitMq__Password` in the API process environment. Use the same password as Compose. The checked-in password is a placeholder and is rejected at startup.
3. Start the broker with `docker compose up -d rabbitmq`. This adds a dedicated persistent volume; it does not change the PostgreSQL or Redis volumes.
4. Run the API with its existing database, Redis, and authentication settings. Messaging is registered in `Program.cs`; no extra worker activation is needed.

Docker Compose reads `.env` for the broker. The API does not read that file automatically; its password must also be configured through User Secrets or the API environment.

The API runs on the host by default and connects to `localhost:5672`. If the API is later containerized on the same Compose network, set `RabbitMq__HostName=rabbitmq`. Set `RabbitMq__UseTls=true` and the appropriate port for a TLS broker; normal certificate validation remains enabled.

The management UI is available at `http://localhost:15672` using the configured broker credentials. Both host port bindings are restricted to loopback for local development.

## Delivery behavior

- RabbitMQ.Client 7 uses `IChannel`, replacing the earlier `IModel` API. All connection, topology, and publishing operations are awaited.
- A singleton publisher lazily opens one connection and one publishing channel. A semaphore serializes channel access, including the confirmation wait.
- Channel creation enables both publisher confirms and confirmation tracking. Every publish uses `mandatory: true`; an ACK is required for success, while a NACK or unroutable return raises an exception.
- Messages are persistent and carry the original message ID, correlation ID, versioned message type, and occurrence timestamp.
- Both publisher and consumer declare the same durable topic exchange `supplyguard.events`, durable queue `notification.queue`, and binding `early-warning.raised.v1`. The queue retains messages while the consumer is offline.
- Closed publishing connections are replaced on the next outbox attempt. Failed or timed-out publishing channels are discarded. Automatic client recovery is disabled; the outbox owns publication retry timing, and the notification consumer owns its reconnect loop.
- The dispatcher marks completion only after confirmation. A crash between confirmation and completion-save can cause redelivery with the same message ID. Consumer deduplication remains necessary.
- Run one outbox processor instance until database row claiming is implemented.

## Notification consumer

- The consumer owns a separate connection and receiving channel. Its channel has dispatch concurrency one, `autoAck: false`, and per-consumer prefetch controlled by `RabbitMq:ConsumerPrefetchCount` (default 10, validated between 1 and 1000).
- It deserializes and validates `EarlyWarningRaisedV1`, including message/header identity, then writes `[Notification Sent]` with structured `MessageId`, `WarningId`, `SupplierId`, and `Severity` fields. Only then does it acknowledge that delivery with `multiple: false`.
- It fully uses the client's delivery-body memory within the awaited callback, retaining no body reference after the callback returns.
- A malformed contract or processing failure is copied to the durable error exchange `supplyguard.notification.errors` and queue `notification.error.queue`. A separate, confirm-enabled channel publishes the persistent original payload and transport identifiers with sanitized failure metadata. Only after this publication is confirmed does the receiving channel call `BasicNackAsync` with `requeue: false`.
- This explicit error copy leaves the existing main queue's arguments unchanged. It requires no queue deletion or dead-letter-policy migration. Error messages remain available for inspection and deliberate replay; there is no automatic replay loop.
- If error publication or ACK/NACK settlement fails, the consumer abandons the session and closes its channels. The original delivery remains unacknowledged for broker redelivery. Connection/channel shutdown and server-side cancellation also trigger fresh topology declaration, QoS setup, and subscription after `RabbitMq:ConsumerReconnectDelay` (default five seconds).
- Host shutdown cancels in-flight work and disposes the consumer resources. Successful logs can repeat if a delivery is redelivered; this console proof of concept does not implement a durable inbox or exactly-once notification effects.

## Validation

Run the solution tests with `dotnet test SupplyGuard.sln`. Controlled tests verify confirmation waits, mandatory delivery, persistent metadata, publisher/consumer channel isolation, bounded prefetch, structured logging before ACK, quarantine confirmation before NACK, initial connection failures, broker shutdown, server cancellation, settlement failure, and hosted-service registration.

The live RabbitMQ tests are skipped by default. With a broker running, configure `RabbitMq__Password` and any host/port/user/vhost/TLS overrides, then set `SUPPLYGUARD_RUN_RABBITMQ_TESTS=1`. Run:

```powershell
dotnet test tests/SupplyGuard.IntegrationTests/SupplyGuard.IntegrationTests.csproj --filter FullyQualifiedName~RabbitMqLiveTests
```

The publisher live test reads the durable queue and checks that an unbound routing key raises a publish exception. The consumer live test exercises the concrete publisher and hosted consumer together, verifies all structured log fields, sends a poison message, checks its persistent error copy, then stops the consumer and verifies that no unsettled original is requeued. Both tests use unique topology names and remove only their owned queues and exchanges afterward. They do not consume or delete the application's notification queue.
