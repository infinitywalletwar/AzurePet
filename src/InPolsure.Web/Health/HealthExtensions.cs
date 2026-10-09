using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace InPolsure.Web.Health;

/// <summary>
/// Registers and maps the liveness and readiness endpoints (lab-01 §6.2).
/// </summary>
public static class HealthExtensions
{
    /// <summary>
    /// Registers the health check service. No checks are added here: liveness has none, and
    /// readiness checks (tagged <see cref="HealthEndpoints.ReadyTag"/>) are added by later features.
    /// </summary>
    public static IServiceCollection AddInPolsureHealth(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHealthChecks();

        return services;
    }

    /// <summary>
    /// Maps <c>/health/live</c> and <c>/health/ready</c>: anonymous, never cached, plain-text status,
    /// GET/HEAD only, and excluded from the <c>http.server.*</c> metrics.
    /// </summary>
    public static IEndpointRouteBuilder MapInPolsureHealth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // AllowCachingResponses = false (the default) makes the middleware send
        // Cache-Control: no-store, no-cache. The default writer returns the status as text/plain.
        endpoints.MapHealthChecks(HealthEndpoints.LivePath, new HealthCheckOptions
        {
            Predicate = static _ => false,
            AllowCachingResponses = false,
        })
            .WithHealthProbeConventions();

        endpoints.MapHealthChecks(HealthEndpoints.ReadyPath, new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains(HealthEndpoints.ReadyTag),
            AllowCachingResponses = false,
        })
            .WithHealthProbeConventions();

        return endpoints;
    }

    // Probes are anonymous and read-only; frequent probe traffic must not skew the
    // http.server.* metrics or the 5xx-rate alert built on them.
    private static void WithHealthProbeConventions(this IEndpointConventionBuilder builder) =>
        builder
            .AllowAnonymous()
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]))
            .DisableHttpMetrics();
}
