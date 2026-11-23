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
    public async Task ResponseWithShapContributions_IsRetained_AndIdenticalRedeliveryIsDuplicate()
    {
        await using var store = await JitRiskAnalysisTests.Store.CreateAsync();
        var supplier = new Supplier("Worker Supplier", "WORKER-SHAP", "TR");
        var request = WorkerRiskAnalysisFixtures.Request(supplier.Id);
        await using (var seed = store.Context())
        {
            seed.Suppliers.Add(supplier);
            seed.XAIAuditLogs.Add(XAIAuditLog.CreatePending(supplier.Id, request.CorrelationId, request.MessageId,
                new IntegrationMessageSerializer().Serialize(request), request.OccurredAtUtc));
            await seed.SaveChangesAsync();
        }
        var response = WorkerRiskAnalysisFixtures.ResponseWithShap(request);

        Assert.Equal(RiskAnalysisResponseDisposition.Processed, (await HandleAsync(store, response)).Value!.Disposition);
        // A redelivery arrives as a new object with a new list instance: equality must compare contents.
        var redelivered = new RiskAnalysisResponseSerializer().Deserialize(new RiskAnalysisResponseSerializer().Serialize(response));
        Assert.NotSame(response.Contributions, redelivered.Contributions);
        Assert.Equal(RiskAnalysisResponseDisposition.Duplicate, (await HandleAsync(store, redelivered)).Value!.Disposition);
        var changed = response with { Contributions = [.. response.Contributions!.Take(5), new("ActiveRiskIndicatorsCount", 9m)] };
        Assert.Equal("RiskAnalysis.ConflictingResponse", Assert.Single((await HandleAsync(store, changed)).Errors).Code);

        await using var verification = store.Context();
        var audit = await verification.XAIAuditLogs.SingleAsync();
        Assert.Equal(response, new RiskAnalysisResponseSerializer().Deserialize(audit.ResponsePayload!));
    }

    [Fact]
    public void ResponseContract_AcceptsPythonShapPayload_IncludingExponentNumbersAndExplicitNulls()
    {
        var serializer = new RiskAnalysisResponseSerializer();
        var supplier = Guid.NewGuid().ToString("D");
        var success = serializer.Deserialize(
            "{\"CorrelationId\":\"c-1\",\"SupplierId\":\"" + supplier + "\",\"ConfidenceScore\":3.1e-05," +
            "\"ExplanationText\":\"x\",\"IsSuccessful\":true,\"ErrorMessage\":null,\"BaseValue\":-2.5," +
            "\"Contributions\":[{\"Feature\":\"qualityDefectRate\",\"Contribution\":1e-06}," +
            "{\"Feature\":\"averageDeliveryDelayDays\",\"Contribution\":-0.75}]}");
        Assert.Equal(.000001m, success.Contributions![0].Contribution);
        Assert.Equal(-2.5m, success.BaseValue);

        var failure = serializer.Deserialize(
            "{\"CorrelationId\":\"c-2\",\"SupplierId\":\"" + supplier + "\",\"ConfidenceScore\":0.0,\"ExplanationText\":\"\"," +
            "\"IsSuccessful\":false,\"ErrorMessage\":\"Risk analizi tamamlanamadı.\",\"BaseValue\":null,\"Contributions\":null}");
        Assert.Null(failure.Contributions);
    }

    [Theory]
    [InlineData("on-failure")]
    [InlineData("base-without-contributions")]
    [InlineData("contributions-without-base")]
    [InlineData("duplicate-feature")]
    [InlineData("null-item")]
    [InlineData("unknown-item-member")]
    [InlineData("quoted-contribution")]
    [InlineData("empty-list")]
    public void ResponseContract_RejectsInvalidShapDecomposition(string scenario)
    {
        var serializer = new RiskAnalysisResponseSerializer();
        var node = JsonNode.Parse(serializer.Serialize(WorkerRiskAnalysisFixtures.ResponseWithShap(WorkerRiskAnalysisFixtures.Request())))!.AsObject();
        var items = node["Contributions"]!.AsArray();
        switch (scenario)
        {
            case "on-failure":
                node["IsSuccessful"] = false; node["ConfidenceScore"] = 0; node["ExplanationText"] = ""; node["ErrorMessage"] = "failed";
                break;
            case "base-without-contributions": node["Contributions"] = null; break;
            case "contributions-without-base": node["BaseValue"] = null; break;
            case "duplicate-feature": items[1]!["Feature"] = items[0]!["Feature"]!.GetValue<string>(); break;
            case "null-item": items.Add(null); break;
            case "unknown-item-member": items[0]!["Extra"] = 1; break;
            case "quoted-contribution": items[0]!["Contribution"] = "1.5"; break;
            case "empty-list": node["Contributions"] = new JsonArray(); break;
        }
        Assert.Throws<JsonException>(() => serializer.Deserialize(node.ToJsonString()));
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
    public static RiskAnalysisResponse ResponseWithShap(RiskAnalysisRequest request) => Response(request) with
    {
        BaseValue = -1.234567m,
        Contributions =
        [
            new("averageDeliveryDelayDays", 1.5m),
            new("onTimeDeliveryRate", -.25m),
            new("qualityDefectRate", 2.000001m),
            new("DeliveryDelayTrend_Percentage", 1.1m),
            new("FinancialStabilityScore", -.4m),
            new("ActiveRiskIndicatorsCount", .3m)
        ]
    };
}
