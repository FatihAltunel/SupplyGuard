namespace SupplyGuard.Application.Common.Interfaces;

public interface IFinancialIntegrationService
{
    /// <summary>Returns stability in [0, 100]; higher means more stable. Missing data must fail explicitly.</summary>
    Task<decimal> GetStabilityScoreAsync(Guid supplierId, CancellationToken cancellationToken = default);
}
