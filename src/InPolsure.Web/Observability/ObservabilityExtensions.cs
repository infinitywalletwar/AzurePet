using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace InPolsure.Web.Observability;

/// <summary>
/// OpenTelemetry baseline for the web host (ADR-0010 items 1, 3, 4, 10; lab-01 §6.3).
/// </summary>
/// <remarks>
/// <para>
/// Takes <see cref="IHostApplicationBuilder"/> instead of the usual
/// <c>(IServiceCollection, IConfiguration)</c> pair because it also needs the logging
/// builder (OTel logging provider, JSON console) and the host environment. Call it first in
/// <c>Program.cs</c>, before any other feature registration, so that every later component
/// logs and traces through the configured providers.
/// </para>
/// <para>
/// Export: the OTLP exporter is registered for all signals only when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (read from <see cref="IConfiguration"/>, which
/// includes environment variables). Otherwise no exporter is registered and nothing leaves
/// the process (tests, CI). Further exporters (Azure Monitor in Lab 02, in-memory in tests)
/// are added with <c>services.ConfigureOpenTelemetryTracerProvider(...)</c> and friends.
/// </para>
/// <para>
/// Privacy (NFR-042): the ASP.NET Core and HttpClient instrumentations in use (1.19.0) replace
/// query-string values with <c>Redacted</c> by default (<c>url.query</c>, <c>url.full</c>);
/// that default is kept, and no request or response bodies are recorded.
/// </para>
/// </remarks>
public static class ObservabilityExtensions
{
    /// <summary>The OpenTelemetry <c>service.name</c> resource attribute.</summary>
    public const string ServiceName = "inpolsure-web";

    /// <summary>Configuration key (environment variable) that enables the OTLP exporter.</summary>
    public const string OtlpEndpointConfigurationKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Adds OpenTelemetry tracing, metrics and logging with the InPolsure resource, request
    /// filtering, conditional OTLP export and, outside Development, the JSON console formatter.
    /// </summary>
    public static TBuilder AddInPolsureObservability<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: ServiceName,
                serviceVersion: GetServiceVersion()))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = TraceRequestFilter.ShouldTrace)
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithLogging(
                configureBuilder: null,
                configureOptions: options =>
                {
                    options.IncludeScopes = true;
                    options.IncludeFormattedMessage = true;
                });

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointConfigurationKey]))
        {
            openTelemetry.UseOtlpExporter();
        }

        if (!builder.Environment.IsDevelopment())
        {
            // Containers: one JSON object per line. Scopes carry TraceId/SpanId (the host's
            // default ActivityTrackingOptions), so console logs correlate with traces.
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
            });
        }

        return builder;
    }

    private static string GetServiceVersion() =>
        typeof(ObservabilityExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
