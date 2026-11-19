using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;
using SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisResponse;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Infrastructure.Messaging.Serialization;
using SupplyGuard.Infrastructure.Persistence;
using SupplyGuard.Infrastructure.Persistence.Repositories;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class WorkerRiskAnalysisPersistenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Response_CommitsOutcome_ThenRedeliveryIsNoOp_AndConflictingResponseIsRejected(bool success)
    {
        await using var store = await JitRiskAnalysisTests.Store.CreateAsync();
        var supplier = new Supplier("Worker Supplier", "WORKER-1", "TR");
        var request = WorkerRiskAnalysisFixtures.Request(supplier.Id);
        await using (var seed = store.Context())
        {
            seed.Suppliers.Add(supplier);
            seed.XAIAuditLogs.Add(XAIAuditLog.CreatePending(supplier.Id, request.CorrelationId, request.MessageId,
                new IntegrationMessageSerializer().Serialize(request), request.OccurredAtUtc));
            await seed.SaveChangesAsync();
        }
        var response = WorkerRiskAnalysisFixtures.Response(request) with
        {
            IsSuccessful = success, ConfidenceScore = success ? .996123456m : 0,
            ExplanationText = success ? "Risk açıklaması" : "", ErrorMessage = success ? null : "Model failed"
        };
        Assert.Equal(RiskAnalysisResponseDisposition.Processed, (await HandleAsync(store, response)).Value!.Disposition);
        // A new scope/context models an ACK lost after the database commit.
        Assert.Equal(RiskAnalysisResponseDisposition.Duplicate, (await HandleAsync(store, response)).Value!.Disposition);
        var conflict = response with { ExplanationText = success ? "Different result" : "", ErrorMessage = success ? null : "Different error" };
        Assert.Equal("RiskAnalysis.ConflictingResponse", Assert.Single((await HandleAsync(store, conflict)).Errors).Code);

        await using var verification = store.Context();
        var audit = await verification.XAIAuditLogs.SingleAsync();
        Assert.Equal(success ? ExplanationStatus.Completed : ExplanationStatus.Failed, audit.ExplanationStatus);
        Assert.Equal(response.ExplanationText, audit.ExplanationText);
        Assert.Equal(response, new RiskAnalysisResponseSerializer().Deserialize(audit.ResponsePayload!));
        Assert.Equal(request.MessageId, audit.RequestMessageId);
        Assert.Empty(await verification.RiskAssessments.ToListAsync());
    }

    [Fact]
    public async Task UnknownCorrelationOrWrongSupplier_DoesNotCreateOrAlterAudit()
    {
        await using var store = await JitRiskAnalysisTests.Store.CreateAsync();
        var supplier = new Supplier("Worker Supplier", "WORKER-2", "TR");
        var request = WorkerRiskAnalysisFixtures.Request(supplier.Id);
        await using (var seed = store.Context())
        {
            seed.Suppliers.Add(supplier);
            seed.XAIAuditLogs.Add(XAIAuditLog.CreatePending(supplier.Id, request.CorrelationId, request.MessageId, "{}", request.OccurredAtUtc));
            await seed.SaveChangesAsync();
        }
        var response = WorkerRiskAnalysisFixtures.Response(request);
        Assert.False((await HandleAsync(store, response with { CorrelationId = "unknown" })).IsSuccess);
        Assert.False((await HandleAsync(store, response with { SupplierId = Guid.NewGuid() })).IsSuccess);
        await using var verification = store.Context();
        Assert.Equal(ExplanationStatus.Pending, (await verification.XAIAuditLogs.SingleAsync()).ExplanationStatus);
    }

    [Fact]
    public async Task PendingAudit_RejectsIdentityTampering_AndConcurrentStaleUpdate()
    {
        await using var store = await JitRiskAnalysisTests.Store.CreateAsync();
        var supplier = new Supplier("Worker Supplier", "WORKER-3", "TR");
        await using (var seed = store.Context())
        {
            seed.Suppliers.Add(supplier);
            seed.XAIAuditLogs.Add(XAIAuditLog.CreatePending(supplier.Id, "req-concurrent", Guid.NewGuid(), "{}", WorkerRiskAnalysisFixtures.Now));
            await seed.SaveChangesAsync();
        }
        await using (var tampering = store.Context())
        {
            var audit = await tampering.XAIAuditLogs.SingleAsync();
            audit.Complete(.9m, "Result", "{}", WorkerRiskAnalysisFixtures.Now);
            tampering.Entry(audit).Property(x => x.CorrelationId).CurrentValue = "tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => tampering.SaveChangesAsync());
        }
        await using var first = store.Context();
        await using var second = store.Context();
        var firstAudit = await first.XAIAuditLogs.SingleAsync();
        var secondAudit = await second.XAIAuditLogs.SingleAsync();
        firstAudit.Complete(.9m, "First", "{}", WorkerRiskAnalysisFixtures.Now);
        secondAudit.Fail("Second", "{}", WorkerRiskAnalysisFixtures.Now);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Theory]
    [InlineData("missing-success")]
    [InlineData("duplicate")]
    [InlineData("quoted-score")]
    [InlineData("unknown")]
    [InlineData("wrong-case")]
    [InlineData("out-of-range")]
    public void ResponseContract_RejectsInvalidWireValues(string scenario)
    {
        var serializer = new RiskAnalysisResponseSerializer();
        var json = serializer.Serialize(WorkerRiskAnalysisFixtures.Response(WorkerRiskAnalysisFixtures.Request()));
        var node = JsonNode.Parse(json)!.AsObject();
        switch (scenario)
        {
            case "missing-success": node.Remove("IsSuccessful"); break;
            case "quoted-score": node["ConfidenceScore"] = "0.9"; break;
            case "out-of-range": node["ConfidenceScore"] = 2; break;
            case "unknown": node["MessageId"] = "unexpected"; break;
            case "wrong-case": node["confidenceScore"] = node["ConfidenceScore"]!.DeepClone(); node.Remove("ConfidenceScore"); break;
            case "duplicate": json = json.Replace("\"IsSuccessful\":true", "\"IsSuccessful\":true,\"IsSuccessful\":true"); break;
        }
        Assert.Throws<JsonException>(() => serializer.Deserialize(scenario == "duplicate" ? json : node.ToJsonString()));
    }

    private static async Task<SupplyGuard.Application.Common.CQRS.Result<ProcessRiskAnalysisResponseResult>> HandleAsync(
        JitRiskAnalysisTests.Store store, RiskAnalysisResponse response)
    {
        await using var context = store.Context();
        await using var unit = new EfUnitOfWork(context);
        return await new ProcessRiskAnalysisResponseCommandHandler(new XAIAuditLogRepository(context),
            new RiskAnalysisResponseSerializer(), unit, TimeProvider.System).HandleAsync(new(response));
    }
}

internal static class WorkerRiskAnalysisFixtures
{
    public static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    public static RiskAnalysisRequest Request(Guid? supplierId = null) => new("request-123", supplierId ?? Guid.NewGuid(),
        2.4m, .95m, .004m, 15.2m, 80, 3) { MessageId = Guid.NewGuid(), OccurredAtUtc = Now };
    public static RiskAnalysisResponse Response(RiskAnalysisRequest request) =>
        new(request.CorrelationId, request.SupplierId, .996m, "Risk açıklaması", true, null);
}
