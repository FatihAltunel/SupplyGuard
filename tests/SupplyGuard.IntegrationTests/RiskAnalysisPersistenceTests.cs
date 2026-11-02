using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using SupplyGuard.Application.Common.Caching;
using SupplyGuard.Application.Common.Exceptions;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;
using SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisCompleted;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Infrastructure.Messaging.Serialization;
using SupplyGuard.Infrastructure.Persistence;
using SupplyGuard.Infrastructure.Persistence.Configurations;
using SupplyGuard.Infrastructure.Persistence.Outbox;
using SupplyGuard.Infrastructure.Persistence.Repositories;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class RiskAnalysisPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_PersistsCorrectOutcome_AndDuplicateRemainsSafeWhenRedisFails(bool terminalFailure)
    {
        await using var store = await Store.CreateAsync();
        var supplier = new Supplier("Supplier", "PERSIST-1", "TR");
        var request = RiskAnalysisFixtures.Requested(supplier.Id);
        var serializer = new IntegrationMessageSerializer();
        await using (var seed = store.Context())
        {
            seed.Suppliers.Add(supplier);
            await new OutboxWriter(seed, serializer).WriteAsync(request);
            await seed.SaveChangesAsync();
        }
        await using var context = store.Context();
        await using var unit = new EfUnitOfWork(context);
        var repository = new RiskManagementRepository(context);
        var inner = new CreateProcessRiskAnalysisCompletedCommandHandler(repository, new OutboxReader(context), serializer,
            new RiskAnalysisRequestedEventValidator(new RiskAnalysisInputV1Validator()), unit);
        var cache = new Mock<IDistributedCache>();
        cache.Setup(value => value.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Redis is down"));
        var handler = new CacheInvalidatingCommandHandlerDecorator<ProcessRiskAnalysisCompletedCommand, ProcessRiskAnalysisCompletedResult>(
            inner, cache.Object, command => [CacheKeys.SupplierDetails(command.Completion.SupplierId)],
            NullLogger<CacheInvalidatingCommandHandlerDecorator<ProcessRiskAnalysisCompletedCommand, ProcessRiskAnalysisCompletedResult>>.Instance);
        var completion = RiskAnalysisFixtures.Completed(request);
        if (terminalFailure) completion = completion with { Status = "failed", Result = null, Error = new("DUMMY_PROCESSING_FAILED", "Dummy failed.") };

        var result = await handler.HandleAsync(new(completion));
        var duplicate = await handler.HandleAsync(new(completion));

        Assert.True(result.IsSuccess);
        Assert.Equal(RiskAnalysisCompletionDisposition.Duplicate, duplicate.Value!.Disposition);
        cache.Verify(value => value.RemoveAsync(CacheKeys.SupplierDetails(supplier.Id), It.IsAny<CancellationToken>()), Times.Exactly(2));
        await using var verification = store.Context();
        var audit = await verification.XAIAuditLogs.SingleAsync();
        Assert.Equal(!terminalFailure, audit.IsSuccessful);
        Assert.Equal(terminalFailure ? ExplanationStatus.Failed : ExplanationStatus.Completed, audit.ExplanationStatus);
        Assert.Equal(terminalFailure ? 0 : 1, await verification.RiskAssessments.CountAsync());
        Assert.Equal(request.CorrelationId.ToString("D"), audit.CorrelationId);
        Assert.Contains(".000Z", audit.RequestPayload);
        Assert.Contains("\"isMock\":true", audit.ResponsePayload!);
        if (terminalFailure)
        {
            Assert.Null(audit.RiskAssessmentId);
            Assert.Null((await verification.Suppliers.SingleAsync()).LastModifiedAtUtc);
        }
        else Assert.Equal(RiskLevel.High, (await verification.RiskAssessments.SingleAsync()).OverallRiskLevel);
    }

    [Fact]
    public async Task DuplicateConstraint_RollsBackAssessmentAndDiscardsTrackedState()
    {
        await using var store = await Store.CreateAsync();
        var supplier = new Supplier("Supplier", "PERSIST-2", "TR");
        var correlation = Guid.NewGuid().ToString("D");
        await using (var seed = store.Context())
        {
            seed.Suppliers.Add(supplier);
            seed.XAIAuditLogs.Add(Audit(supplier.Id, correlation));
            await seed.SaveChangesAsync();
        }
        await using var context = store.Context();
        await using var unit = new EfUnitOfWork(context);
        await unit.BeginTransactionAsync();
        var assessment = RiskAssessment.FromModelScore(supplier.Id, 65m, RiskAnalysisFixtures.Now, "Dummy rationale.");
        context.RiskAssessments.Add(assessment);
        context.XAIAuditLogs.Add(Audit(supplier.Id, correlation, assessment.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => unit.CommitAsync());
        await unit.RollbackAsync();
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(0, await context.RiskAssessments.CountAsync());
        Assert.Equal(1, await context.XAIAuditLogs.CountAsync());
    }

    [Fact]
    public async Task SoftDeletedSupplier_DoesNotHideCorrelationFromIdempotencyLookup()
    {
        await using var store = await Store.CreateAsync();
        await using var context = store.Context();
        var supplier = new Supplier("Supplier", "PERSIST-3", "TR");
        var correlation = Guid.NewGuid().ToString("D");
        context.Suppliers.Add(supplier);
        context.XAIAuditLogs.Add(Audit(supplier.Id, correlation));
        await context.SaveChangesAsync();
        supplier.MarkAsDeleted();
        await context.SaveChangesAsync();
        Assert.Empty(await context.XAIAuditLogs.ToListAsync());
        Assert.True(await new RiskManagementRepository(context).AuditExistsByCorrelationIdAsync(correlation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditHistory_CannotBeUpdatedOrDeleted(bool delete)
    {
        await using var store = await Store.CreateAsync();
        await using var context = store.Context();
        var supplier = new Supplier("Supplier", "PERSIST-4", "TR");
        var audit = Audit(supplier.Id, Guid.NewGuid().ToString("D"));
        context.Suppliers.Add(supplier);
        context.XAIAuditLogs.Add(audit);
        await context.SaveChangesAsync();
        if (delete) context.XAIAuditLogs.Remove(audit);
        else context.Entry(audit).Property(value => value.ModelVersion).CurrentValue = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyNamedPostgresCorrelationConflict_IsTranslated(bool matchingConstraint)
    {
        await using var store = await Store.CreateAsync();
        var constraint = matchingConstraint ? XAIAuditLogConfiguration.CorrelationIdIndexName : "another_unique_constraint";
        await using var context = store.Context(new UniqueFailure(constraint));
        await using var unit = new EfUnitOfWork(context);
        var correlation = Guid.NewGuid();
        context.XAIAuditLogs.Add(Audit(Guid.NewGuid(), correlation.ToString("D")));
        await unit.BeginTransactionAsync();
        if (matchingConstraint)
        {
            var exception = await Assert.ThrowsAsync<DuplicateCorrelationException>(() => unit.CommitAsync());
            Assert.Equal(correlation, exception.CorrelationId);
        }
        else await Assert.ThrowsAsync<DbUpdateException>(() => unit.CommitAsync());
        await unit.RollbackAsync();
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static XAIAuditLog Audit(Guid supplierId, string correlation, Guid? assessmentId = null) => new(
        supplierId, "dummy", "0.1", correlation, "{}", "{}", 0.9m, 2000, true, RiskAnalysisFixtures.Now, assessmentId);

    private sealed class UniqueFailure(string constraint) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated unique violation", new PostgresException("Duplicate", "ERROR", "ERROR",
                PostgresErrorCodes.UniqueViolation, constraintName: constraint));
    }

    private sealed class Store(SqliteConnection connection, IModel model) : IAsyncDisposable
    {
        public static async Task<Store> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var modelContext = new ModelContext(new DbContextOptionsBuilder<ModelContext>().UseSqlite(connection).Options);
            var store = new Store(connection, modelContext.Model);
            await using var context = store.Context();
            await context.Database.EnsureCreatedAsync();
            return store;
        }

        public SupplyGuardDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<SupplyGuardDbContext>().UseSqlite(connection).UseModel(model);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            return new(options.Options);
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class ModelContext(DbContextOptions<ModelContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfiguration(new SupplierConfiguration());
            builder.ApplyConfiguration(new RiskAssessmentConfiguration());
            builder.ApplyConfiguration(new RiskScoreConfiguration());
            builder.ApplyConfiguration(new RiskIndicatorConfiguration());
            builder.ApplyConfiguration(new EarlyWarningConfiguration());
            builder.ApplyConfiguration(new XAIAuditLogConfiguration());
            builder.ApplyConfiguration(new OutboxMessageConfiguration());
            var converter = new ValueConverter<DateTimeOffset, long>(value => value.UtcTicks, value => new(value, TimeSpan.Zero));
            foreach (var property in builder.Model.GetEntityTypes().SelectMany(type => type.GetProperties()))
            {
                if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) != typeof(DateTimeOffset)) continue;
                property.SetValueConverter(converter);
                property.SetColumnType("INTEGER");
            }
        }
    }
}
