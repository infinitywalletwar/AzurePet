using System.Diagnostics;
using System.Net;
using System.Reflection;
using InPolsure.Web.Observability;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;

namespace InPolsure.Web.IntegrationTests.Observability;

// AC-11 on a minimal host (lab-01 T-01.4). The ASP.NET Core activity source is process-wide,
// so every host's tracer provider sees every request in the test assembly. Assumption: no other
// host serves requests while these tests run. The non-parallel collection below guarantees it
// (xUnit runs it alone, after the parallel collections), so a filtered span can't be recorded
// through another host's provider. Unique probe paths additionally keep positive assertions
// independent of each other.
[Collection(ObservabilityTestGroup.Name)]
public sealed class ObservabilityTests
{
    [Fact]
    public async Task Request_to_normal_path_produces_server_span_with_inpolsure_service_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await ObservabilityTestHost.StartAsync(ct);
        using var client = host.CreateClient();
        var path = UniqueProbePath();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var span = await host.WaitForServerSpanAsync(path, ct);
        Assert.Equal(ActivityKind.Server, span.Kind);
        var resource = host.Provider.GetRequiredService<TracerProvider>().GetResource();
        var attributes = resource.Attributes.ToDictionary(a => a.Key, a => a.Value);
        Assert.Equal("inpolsure-web", attributes["service.name"]);
        Assert.Equal(ExpectedServiceVersion(), attributes["service.version"]);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Request_to_health_endpoint_produces_no_span(string healthPath)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await ObservabilityTestHost.StartAsync(ct);
        using var client = host.CreateClient();

        using var healthResponse = await client.GetAsync(new Uri(healthPath, UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

        // A traced request after the health request: once its span is exported, the health
        // span would have been exported too (requests are sequential).
        var probePath = UniqueProbePath();
        using var probeResponse = await client.GetAsync(new Uri(probePath, UriKind.Relative), ct);
        await host.WaitForServerSpanAsync(probePath, ct);

        Assert.Empty(host.ServerSpans(healthPath));
    }

    [Theory]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_content/InPolsure.Ui/base.css")]
    [InlineData("/app.css")]
    [InlineData("/favicon.ico")]
    [InlineData("/InPolsure.Web.styles.css")]
    public async Task Request_to_static_asset_produces_no_span(string assetPath)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await ObservabilityTestHost.StartAsync(ct);
        using var client = host.CreateClient();

        // The asset does not exist on the minimal host (404); the filter is path-based and
        // runs before routing, so the outcome is the same as for a real static file.
        using var assetResponse = await client.GetAsync(new Uri(assetPath, UriKind.Relative), ct);

        var probePath = UniqueProbePath();
        using var probeResponse = await client.GetAsync(new Uri(probePath, UriKind.Relative), ct);
        await host.WaitForServerSpanAsync(probePath, ct);

        Assert.Empty(host.ServerSpans(assetPath));
    }

    [Fact]
    public async Task Request_with_query_string_records_redacted_query_values()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await ObservabilityTestHost.StartAsync(ct);
        using var client = host.CreateClient();
        var path = UniqueProbePath();

        using var response = await client.GetAsync(new Uri($"{path}?token=secret-value&email=a%40b.example", UriKind.Relative), ct);

        var span = await host.WaitForServerSpanAsync(path, ct);
        Assert.Equal("?token=Redacted&email=Redacted", span.GetTagItem("url.query"));
        Assert.DoesNotContain(span.TagObjects, tag => tag.Value is string value && value.Contains("secret-value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddInPolsureObservability_otlp_endpoint_unset_registers_no_otlp_exporter()
    {
        await using var host = ObservabilityTestHost.Create(otlpEndpoint: null);

        Assert.Empty(OtlpExporterRegistrations(host.Services));
    }

    [Fact]
    public async Task AddInPolsureObservability_otlp_endpoint_set_registers_otlp_exporter()
    {
        // Positive control for the check above: proves that the registration scan can detect
        // UseOtlpExporter(). The host is built but never started, so nothing is exported.
        await using var host = ObservabilityTestHost.Create(otlpEndpoint: "http://localhost:4317");

        Assert.NotEmpty(OtlpExporterRegistrations(host.Services));
    }

    [Theory]
    [InlineData("Testing", "json")]
    [InlineData("Production", "json")]
    [InlineData("Development", "simple")]
    public async Task AddInPolsureObservability_environment_selects_console_formatter(string environmentName, string expectedFormatter)
    {
        await using var host = ObservabilityTestHost.Create(environmentName: environmentName);

        var options = host.Provider.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;

        Assert.Equal(expectedFormatter, options.FormatterName ?? "simple");
    }

    private static string UniqueProbePath() => $"/probe/{Guid.NewGuid():N}";

    private static string ExpectedServiceVersion() =>
        typeof(ObservabilityExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    /// <summary>
    /// Service registrations that involve a type from the OTLP exporter assembly
    /// (<c>UseOtlpExporter()</c> registers its options and <c>UseOtlpExporterRegistration</c>).
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
