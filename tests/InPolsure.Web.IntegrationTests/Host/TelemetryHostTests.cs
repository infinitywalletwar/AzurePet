using System.Diagnostics;
using System.Net;
using InPolsure.Web.Health;
using InPolsure.Web.IntegrationTests.Observability;
using InPolsure.Web.Observability;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using static InPolsure.Web.IntegrationTests.Host.HostResponse;

namespace InPolsure.Web.IntegrationTests.Host;

// AC-11 on the real host (lab-01 T-01.8). ActivitySource listeners are process-wide, so these tests share the
// non-parallel Observability collection: no other host serves requests while they run, and every exported span
// comes from this test's host.
[Collection(ObservabilityTestGroup.Name)]
public sealed class TelemetryHostTests
{
    private static readonly TimeSpan _spanTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Get_root_produces_server_span_with_inpolsure_service_name()
    {
        var exported = new SynchronizedList<Activity>();
        await using var factory = new InPolsureWebFactory(
            configureTestServices: services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(exported)));
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, "/");
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var span = await WaitForServerSpanAsync(exported, "/");
        Assert.Equal(ActivityKind.Server, span.Kind);
        var resource = factory.Services.GetRequiredService<TracerProvider>().GetResource();
        var attributes = resource.Attributes.ToDictionary(a => a.Key, a => a.Value);
        Assert.Equal("inpolsure-web", attributes["service.name"]);
    }

    [Fact]
    public async Task Get_health_endpoints_produce_no_span()
    {
        var exported = new SynchronizedList<Activity>();
        await using var factory = new InPolsureWebFactory(
            configureTestServices: services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(exported)));
        using var client = factory.CreateNonRedirectingClient();

        foreach (var path in new[] { HealthEndpoints.LivePath, HealthEndpoints.ReadyPath })
        {
            var (response, _) = await GetAsync(client, path);
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        // A traced request afterwards: once its span is exported, health spans would have been exported too.
        var (rootResponse, _) = await GetAsync(client, "/");
        rootResponse.Dispose();
        await WaitForServerSpanAsync(exported, "/");

        Assert.DoesNotContain(exported.Snapshot(), span => PathOf(span).StartsWith("/health", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Host_with_otlp_endpoint_empty_registers_no_otlp_exporter()
    {
        IServiceCollection? registrations = null;
        await using var factory = new InPolsureWebFactory(
            settings: new Dictionary<string, string?> { [ObservabilityExtensions.OtlpEndpointConfigurationKey] = string.Empty },
            configureTestServices: services => registrations = services);

        _ = factory.Services;

        Assert.NotNull(registrations);
        Assert.Empty(OtlpExporterRegistrations(registrations));
    }

    [Fact]
    public async Task Host_with_otlp_endpoint_set_registers_otlp_exporter()
    {
        // Positive control for the test above: proves that settings reach Program.cs before services are
        // registered and that the scan detects UseOtlpExporter(). Nothing listens on the endpoint and no request
        // is sent; export attempts (if any) fail fast with connection refused.
        IServiceCollection? registrations = null;
        await using var factory = new InPolsureWebFactory(
            settings: new Dictionary<string, string?> { [ObservabilityExtensions.OtlpEndpointConfigurationKey] = "http://127.0.0.1:9" },
            configureTestServices: services => registrations = services);

        _ = factory.Services;

        Assert.NotNull(registrations);
        Assert.NotEmpty(OtlpExporterRegistrations(registrations));
    }

    private static async Task<Activity> WaitForServerSpanAsync(SynchronizedList<Activity> exported, string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < _spanTimeout)
        {
            var span = exported.Snapshot()
                .FirstOrDefault(a => a.Kind == ActivityKind.Server && string.Equals(PathOf(a), path, StringComparison.Ordinal));
            if (span is not null)
            {
                return span;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), ct);
        }

        throw new TimeoutException($"No server span for '{path}' was exported within {_spanTimeout}.");
    }

    private static string PathOf(Activity span) => span.GetTagItem("url.path") as string ?? string.Empty;

    /// <summary>
    /// Service registrations that involve a type from the OTLP exporter assembly
    /// (<c>UseOtlpExporter()</c> registers its options and its registration marker).
    /// </summary>
    private static List<ServiceDescriptor> OtlpExporterRegistrations(IServiceCollection services)
    {
        var otlpAssembly = typeof(OtlpExporterOptions).Assembly;

        return services
            .Where(d => d.ServiceType.Assembly == otlpAssembly
                || d.ServiceType.GenericTypeArguments.Any(t => t.Assembly == otlpAssembly))
            .ToList();
    }
}
