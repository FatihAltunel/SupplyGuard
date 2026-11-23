using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.WebAPI.RateLimiting;

public static class RateLimitPolicies
{
    public const string AnalysisSubmit = "analysis-submit";
    public const string AnalysisStatus = "analysis-status";
    public const string Authentication = "authentication";
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;
    public int AnalysisRequestsPerMinute { get; set; } = 10;
    public int StatusReadsPerMinute { get; set; } = 120;
    public int AuthenticationAttemptsPerMinute { get; set; } = 10;
}

public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>Enable only when the API is reachable exclusively through the trusted reverse proxy.</summary>
    public bool TrustForwardedHeaders { get; set; }
}

public static class RateLimitingSetup
{
    /// <summary>Per-client-IP fixed windows. Rejections return 429 with Retry-After and the API error shape.</summary>
    public static IServiceCollection AddSupplyGuardRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new();
        if (settings.AnalysisRequestsPerMinute < 1 || settings.StatusReadsPerMinute < 1 || settings.AuthenticationAttemptsPerMinute < 1)
            throw new InvalidOperationException("RateLimiting limits must be at least 1 request per minute.");

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new[] { new Error("RateLimit.Exceeded", "Too many requests. Please wait and try again.") },
                    cancellationToken);
            };
            options.AddPolicy(RateLimitPolicies.AnalysisSubmit, context => Partition(context, settings, settings.AnalysisRequestsPerMinute));
            options.AddPolicy(RateLimitPolicies.AnalysisStatus, context => Partition(context, settings, settings.StatusReadsPerMinute));
            options.AddPolicy(RateLimitPolicies.Authentication, context => Partition(context, settings, settings.AuthenticationAttemptsPerMinute));
        });
        return services;
    }

    /// <summary>Restores the client IP from X-Forwarded-For when configured; must run before rate limiting.</summary>
    public static WebApplication UseSupplyGuardForwardedHeaders(this WebApplication app)
    {
        var settings = app.Configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>() ?? new();
        if (!settings.TrustForwardedHeaders) return app;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            // Only the single compose proxy hop is trusted. Its container address is not fixed, so the
            // default loopback-only lists are cleared; the API port must not be exposed beyond the host.
            ForwardLimit = 1
        };
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        app.UseForwardedHeaders(options);
        return app;
    }

    private static RateLimitPartition<string> Partition(HttpContext context, RateLimitingOptions settings, int permitsPerMinute)
    {
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!settings.Enabled) return RateLimitPartition.GetNoLimiter(client);
        return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
