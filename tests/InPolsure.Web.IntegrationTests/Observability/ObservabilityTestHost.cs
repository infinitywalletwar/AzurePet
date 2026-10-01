using System.Diagnostics;
using InPolsure.Web.Observability;
using Microsoft.AspNetCore.TestHost;
using OpenTelemetry.Trace;

namespace InPolsure.Web.IntegrationTests.Observability;

/// <summary>
/// Minimal host (lab-01 §6.5) with <see cref="ObservabilityExtensions.AddInPolsureObservability{TBuilder}"/>
/// and an in-memory trace exporter added from the outside, so production code has no test hooks.
/// </summary>
internal sealed class ObservabilityTestHost : IAsyncDisposable
{
    private static readonly TimeSpan _spanTimeout = TimeSpan.FromSeconds(10);

    private readonly WebApplication _app;
    private readonly SynchronizedList<Activity> _exported;

    private ObservabilityTestHost(WebApplication app, SynchronizedList<Activity> exported, IServiceCollection services)
    {
        _app = app;
        _exported = exported;
        Services = services;
    }

    /// <summary>Service registrations of the host (read-only after <c>Build()</c>).</summary>
    public IServiceCollection Services { get; }

    public IServiceProvider Provider => _app.Services;

    public HttpClient CreateClient() => _app.GetTestClient();

    /// <summary>Builds the host without starting it.</summary>
    public static ObservabilityTestHost Create(string? otlpEndpoint = null, string environmentName = "Testing")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.WebHost.UseTestServer();

        // Hermetic configuration: this overrides any OTEL_EXPORTER_OTLP_ENDPOINT inherited from the
        // developer's or CI's environment, without mutating process-wide environment variables
        // (which would leak into tests running in parallel).
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ObservabilityExtensions.OtlpEndpointConfigurationKey] = otlpEndpoint ?? string.Empty,
        });

        builder.AddInPolsureObservability();

        var exported = new SynchronizedList<Activity>();
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(exported));

        var app = builder.Build();
        app.MapGet("/probe/{id}", (string id) => Results.Text(id));
        app.MapGet("/health/live", () => Results.Text("Healthy"));
        app.MapGet("/health/ready", () => Results.Text("Healthy"));

        return new ObservabilityTestHost(app, exported, builder.Services);
    }

    public static async Task<ObservabilityTestHost> StartAsync(CancellationToken cancellationToken)
    {
        var host = Create();
        await host._app.StartAsync(cancellationToken);
        return host;
    }

    /// <summary>
    /// Waits until a server span for <paramref name="path"/> has been exported. The server span
    /// ends after the response is sent, so it can arrive slightly after the client returns.
    /// </summary>
    public async Task<Activity> WaitForServerSpanAsync(string path, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < _spanTimeout)
        {
            var spans = ServerSpans(path);
            var span = spans.Count > 0 ? spans[0] : null;
            if (span is not null)
            {
                return span;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }

        throw new TimeoutException($"No server span for '{path}' was exported within {_spanTimeout}.");
    }

    /// <summary>
    /// Exported server spans for a path. The ASP.NET Core activity source is process-wide, so
    /// spans from hosts in parallel test classes can be exported here too; filter by path.
    /// </summary>
    public IReadOnlyList<Activity> ServerSpans(string path) =>
        _exported.Snapshot()
            .Where(a => a.Kind == ActivityKind.Server
                && string.Equals(a.GetTagItem("url.path") as string, path, StringComparison.Ordinal))
            .ToList();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
