namespace SupplyGuard.Application.Common.Exceptions;

/// <summary>The requested supplier observations cannot currently be obtained from their source.</summary>
public sealed class SupplierIntegrationUnavailableException()
    : Exception("Supplier performance or financial integration is unavailable.");
