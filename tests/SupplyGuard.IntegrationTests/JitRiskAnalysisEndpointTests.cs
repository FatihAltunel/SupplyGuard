using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SupplyGuard.Application;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;
using SupplyGuard.Application.Security;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Infrastructure;
using SupplyGuard.Infrastructure.Integrations;
using SupplyGuard.Infrastructure.Messaging.Serialization;
using SupplyGuard.Infrastructure.Persistence;
using SupplyGuard.Infrastructure.Persistence.Outbox;
using SupplyGuard.Infrastructure.Persistence.Repositories;
using SupplyGuard.WebAPI.Authentication.Authorization;
using SupplyGuard.WebAPI.Controllers;
using SupplyGuard.WebAPI.RateLimiting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class JitRiskAnalysisEndpointTests
{
    [Fact]
    public async Task AuthorizedPost_CommitsPendingAuditAndFlatOutbox_AndReplayPreservesIdentity()
    {
        await using var api = await Api.CreateAsync();
        using var accepted = await api.PostAsync(Body("http-request-1"));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var first = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var messageId = first.RootElement.GetProperty("requestMessageId").GetGuid();
        Assert.Equal(api.SupplierId, first.RootElement.GetProperty("supplierId").GetGuid());
        Assert.Equal("http-request-1", first.RootElement.GetProperty("correlationId").GetString());
        using var replay = await api.PostAsync(Body("http-request-1"));
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        using var second = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(messageId, second.RootElement.GetProperty("requestMessageId").GetGuid());
        using var conflict = await api.PostAsync(Body("http-request-1", .8m));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await using var context = api.Store.Context();
        var audit = await context.XAIAuditLogs.SingleAsync();
        var outbox = await context.OutboxMessages.SingleAsync();
        Assert.Equal(ExplanationStatus.Pending, audit.ExplanationStatus);
        Assert.Equal(messageId, audit.RequestMessageId);
        Assert.Equal(messageId, outbox.Id);
        Assert.Null(outbox.ProcessedAtUtc);
        Assert.Equal(RiskAnalysisRequest.TypeName, outbox.MessageType);
        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(8, payload.RootElement.EnumerateObject().Count());
        Assert.False(payload.RootElement.TryGetProperty("Features", out _));
        Assert.Equal(.95m, payload.RootElement.GetProperty("onTimeDeliveryRate").GetDecimal());
        Assert.Equal(85m, payload.RootElement.GetProperty("FinancialStabilityScore").GetDecimal());
    }

    [Theory]
    [InlineData("missing-metric")]
    [InlineData("missing-correlation")]
    [InlineData("quoted-number")]
    [InlineData("invalid-range")]
    [InlineData("unknown-field")]
    [InlineData("blank-correlation")]
    public async Task InvalidHttpPayload_IsRejectedWithoutAuditOrOutbox(string scenario)
    {
        await using var api = await Api.CreateAsync();
        var body = JsonNode.Parse(Body("invalid-http-request"))!.AsObject();
        switch (scenario)
        {
            case "missing-metric": body.Remove("qualityDefectRate"); break;
            case "missing-correlation": body.Remove("correlationId"); break;
            case "quoted-number": body["onTimeDeliveryRate"] = "0.95"; break;
            case "invalid-range": body["onTimeDeliveryRate"] = 95; break;
            case "unknown-field": body["Features"] = new JsonObject(); break;
            case "blank-correlation": body["correlationId"] = " "; break;
        }
        using var response = await api.PostAsync(body.ToJsonString());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = api.Store.Context();
        Assert.Empty(await context.XAIAuditLogs.ToListAsync());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task MissingSupplier_ReturnsNotFound()
    {
        await using var api = await Api.CreateAsync();
        using var response = await api.PostAsync(Body("missing-supplier"), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task JwtAndPermission_AreRequiredBeforeHandlingRequests()
    {
        await using var api = await Api.CreateAsync();
        api.Client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await api.PostAsync(Body("anonymous"));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        api.Client.DefaultRequestHeaders.Authorization = new("Bearer", Api.Token(withPermission: false));
        using var forbidden = await api.PostAsync(Body("forbidden"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await using var context = api.Store.Context();
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task UnconfiguredSourceAdapters_ReturnServiceUnavailableWithoutCreatingRequests()
    {
        await using var api = await Api.CreateAsync(simulated: false);
        using var response = await api.PostAsync(Body("unconfigured-adapters"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("RiskAnalysis.IntegrationsUnavailable", await response.Content.ReadAsStringAsync());
        await using var context = api.Store.Context();
        Assert.Empty(await context.XAIAuditLogs.ToListAsync());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Swagger_ExposesPostAndRequiredRequestFields()
    {
        await using var api = await Api.CreateAsync();
        using var response = await api.Client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths").GetProperty("/api/suppliers/{supplierId}/risk-analyses").GetProperty("post");
        Assert.True(operation.GetProperty("responses").TryGetProperty("202", out _));
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(nameof(RequestSupplierRiskAnalysis));
        Assert.Equal(4, schema.GetProperty("required").GetArrayLength());
    }

    [Fact]
    public async Task Get_ReturnsPendingWithSentFeatures_ThenTerminalResult()
    {
        await using var api = await Api.CreateAsync();
        (await api.PostAsync(Body("poll-1"))).Dispose();

        using (var pending = await api.GetAsync("poll-1"))
        {
            Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
            using var json = JsonDocument.Parse(await pending.Content.ReadAsStringAsync());
            var root = json.RootElement;
            Assert.Equal("Pending", root.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("confidenceScore").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("explanationText").ValueKind);
            var features = root.GetProperty("features");
            Assert.Equal(.95m, features.GetProperty("onTimeDeliveryRate").GetDecimal());
            Assert.Equal(15m, features.GetProperty("deliveryDelayTrendPercentage").GetDecimal());
            Assert.Equal(85m, features.GetProperty("financialStabilityScore").GetDecimal());
            Assert.Equal(0, features.GetProperty("activeRiskIndicatorsCount").GetInt32());
        }

        await using (var context = api.Store.Context())
        {
            var audit = await context.XAIAuditLogs.SingleAsync();
            audit.Complete(.8731m, "Tedarikçide %87.3 oranında risk tespit edilmiştir.", "{}", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        using var completed = await api.GetAsync("poll-1");
        using var result = JsonDocument.Parse(await completed.Content.ReadAsStringAsync());
        Assert.Equal("Completed", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(.8731m, result.RootElement.GetProperty("confidenceScore").GetDecimal());
        Assert.StartsWith("Tedarikçide", result.RootElement.GetProperty("explanationText").GetString());
        Assert.NotEqual(JsonValueKind.Null, result.RootElement.GetProperty("completedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Get_FailedAnalysis_ExposesFailureMessageWithoutScore()
    {
        await using var api = await Api.CreateAsync();
        (await api.PostAsync(Body("poll-failed"))).Dispose();
        await using (var context = api.Store.Context())
        {
            var audit = await context.XAIAuditLogs.SingleAsync();
            audit.Fail("Risk analizi tamamlanamadı.", "{}", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        using var response = await api.GetAsync("poll-failed");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Failed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Risk analizi tamamlanamadı.", json.RootElement.GetProperty("failureMessage").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("confidenceScore").ValueKind);
    }

    [Fact]
    public async Task Get_UnknownCorrelationOrOtherSupplier_ReturnsNotFound()
    {
        await using var api = await Api.CreateAsync();
        (await api.PostAsync(Body("owned-correlation"))).Dispose();
        using var unknown = await api.GetAsync("does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var otherSupplier = await api.GetAsync("owned-correlation", Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, otherSupplier.StatusCode);
    }

    [Fact]
    public async Task Get_RequiresReadPermission()
    {
        await using var api = await Api.CreateAsync();
        api.Client.DefaultRequestHeaders.Authorization = new("Bearer", Api.Token(Permissions.RiskAssessmentsCreate));
        using var response = await api.GetAsync("any");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Overrides_InDemoMode_ReplaceIntegrationFeaturesInWorkerPayload()
    {
        await using var api = await Api.CreateAsync();
        using var response = await api.PostAsync(Body("override-1", overrides: (42.5m, 12m, 7)));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var context = api.Store.Context();
        using var payload = JsonDocument.Parse((await context.OutboxMessages.SingleAsync()).Payload);
        Assert.Equal(8, payload.RootElement.EnumerateObject().Count());
        Assert.Equal(42.5m, payload.RootElement.GetProperty("DeliveryDelayTrend_Percentage").GetDecimal());
        Assert.Equal(12m, payload.RootElement.GetProperty("FinancialStabilityScore").GetDecimal());
        Assert.Equal(7, payload.RootElement.GetProperty("ActiveRiskIndicatorsCount").GetInt32());
    }

    [Fact]
    public async Task Overrides_OutsideDemoMode_AreRejectedWithoutCreatingRequests()
    {
        await using var api = await Api.CreateAsync(simulated: false);
        using var response = await api.PostAsync(Body("override-denied", overrides: (10m, 50m, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("RiskAnalysis.OverridesNotAllowed", await response.Content.ReadAsStringAsync());
        await using var context = api.Store.Context();
        Assert.Empty(await context.XAIAuditLogs.ToListAsync());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData(-100.01, 50, 0)]
    [InlineData(10, 100.01, 0)]
    [InlineData(10, 50, -1)]
    public async Task Overrides_OutOfRange_AreRejected(double trend, double stability, int count)
    {
        await using var api = await Api.CreateAsync();
        using var response = await api.PostAsync(Body("override-range", overrides: ((decimal)trend, (decimal)stability, count)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = api.Store.Context();
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task RateLimit_RejectsExcessSubmissionsWith429AndRetryAfter_WithoutCreatingRequests()
    {
        await using var api = await Api.CreateAsync(rateLimitPerMinute: 2);
        (await api.PostAsync(Body("limited-1"))).Dispose();
        (await api.PostAsync(Body("limited-2"))).Dispose();
        using var rejected = await api.PostAsync(Body("limited-3"));

        Assert.Equal((HttpStatusCode)429, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Contains("RateLimit.Exceeded", await rejected.Content.ReadAsStringAsync());
        await using var context = api.Store.Context();
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task RateLimit_StatusPollingUsesItsOwnBudget()
    {
        await using var api = await Api.CreateAsync(rateLimitPerMinute: 1);
        using var accepted = await api.PostAsync(Body("separate-budget"));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var firstPoll = await api.GetAsync("separate-budget");
        Assert.Equal(HttpStatusCode.OK, firstPoll.StatusCode);
        using var secondPoll = await api.GetAsync("separate-budget");
        Assert.Equal((HttpStatusCode)429, secondPoll.StatusCode);
    }

    private static string Body(string correlation, decimal deliveryRate = .95m,
        (decimal Trend, decimal Stability, int Count)? overrides = null) => overrides is { } value
        ? JsonSerializer.Serialize(new
        {
            correlationId = correlation,
            averageDeliveryDelayDays = 2.4m,
            onTimeDeliveryRate = deliveryRate,
            qualityDefectRate = .004m,
            deliveryDelayTrendPercentage = value.Trend,
            financialStabilityScore = value.Stability,
            activeRiskIndicatorsCount = value.Count
        })
        : Body(correlation, deliveryRate);

    private static string Body(string correlation, decimal deliveryRate) => JsonSerializer.Serialize(new
    {
        correlationId = correlation, averageDeliveryDelayDays = 2.4m, onTimeDeliveryRate = deliveryRate, qualityDefectRate = .004m
    });

    private sealed class Api(WebApplication application, HttpClient client, JitRiskAnalysisTests.Store store, Guid supplierId) : IAsyncDisposable
    {
        private const string SigningKey = "http-test-only-symmetric-signing-key-32-characters";
        public HttpClient Client => client;
        public JitRiskAnalysisTests.Store Store => store;
        public Guid SupplierId => supplierId;

        public static async Task<Api> CreateAsync(bool simulated = true, int? rateLimitPerMinute = null)
        {
            var store = await JitRiskAnalysisTests.Store.CreateAsync();
            var supplier = new Supplier("HTTP Supplier", Guid.NewGuid().ToString("N"), "TR");
            await using (var seed = store.Context())
            {
                seed.Suppliers.Add(supplier);
                await seed.SaveChangesAsync();
            }
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddControllers().AddApplicationPart(typeof(SupplierRiskAnalysesController).Assembly);
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddJitRiskAnalysis();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddScoped(_ => store.Context());
            builder.Services.AddScoped<ISupplierRepository, SupplierRepository>();
            builder.Services.AddScoped<ISupplierRiskIndicatorReader, SupplierRiskIndicatorReader>();
            builder.Services.AddScoped<IRiskAnalysisRepository, RiskManagementRepository>();
            builder.Services.AddScoped<IXAIAuditLogRepository, XAIAuditLogRepository>();
            builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
            builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
            builder.Services.AddSingleton<IIntegrationMessageSerializer, IntegrationMessageSerializer>();
            if (simulated) builder.Services.AddSimulatedSupplierIntegrations();
            else
            {
                builder.Services.AddScoped<ISupplierPerformanceService, UnavailableSupplierIntegrations>();
                builder.Services.AddScoped<IFinancialIntegrationService, UnavailableSupplierIntegrations>();
            }
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = "test", ValidateAudience = true, ValidAudience = "test",
                    ValidateLifetime = true, ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), ClockSkew = TimeSpan.Zero
                };
            });
            builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
            if (rateLimitPerMinute is { } limit)
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:AnalysisRequestsPerMinute"] = limit.ToString(),
                    ["RateLimiting:StatusReadsPerMinute"] = limit.ToString(),
                    ["RateLimiting:AuthenticationAttemptsPerMinute"] = limit.ToString()
                });
                builder.Services.AddSupplyGuardRateLimiting(builder.Configuration);
            }
            var application = builder.Build();
            application.UseSwagger();
            if (rateLimitPerMinute is not null) application.UseRateLimiter();
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapControllers();
            await application.StartAsync();
            var address = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var client = new HttpClient { BaseAddress = new Uri(address) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(withPermission: true));
            return new(application, client, store, supplier.Id);
        }

        public Task<HttpResponseMessage> PostAsync(string body, Guid? supplier = null) =>
            client.PostAsync($"/api/suppliers/{supplier ?? supplierId}/risk-analyses", new StringContent(body, Encoding.UTF8, "application/json"));

        public Task<HttpResponseMessage> GetAsync(string correlation, Guid? supplier = null) =>
            client.GetAsync($"/api/suppliers/{supplier ?? supplierId}/risk-analyses/{correlation}");

        public static string Token(bool withPermission) => withPermission
            ? Token(Permissions.RiskAssessmentsCreate, Permissions.RiskAssessmentsRead)
            : Token();

        public static string Token(params string[] permissions)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")) };
            claims.AddRange(permissions.Select(permission => new Claim(Permissions.ClaimType, permission)));
            var token = new JwtSecurityToken("test", "test", claims, expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await application.DisposeAsync();
            await store.DisposeAsync();
        }
    }
}
