using InPolsure.Web.Observability;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;

namespace InPolsure.Web.UiTests.Fixtures;

/// <summary>
/// The real host (<c>Program.cs</c> and its <c>appsettings*.json</c>) on Kestrel, so a browser can reach it
/// (lab-01 §6.5): loopback, dynamic port, environment <c>Testing</c> (production-like settings of
/// <c>appsettings.json</c>), probe pages enabled, no OTLP export.
/// </summary>
internal sealed class UiAppFactory : WebApplicationFactory<Program>
{
    public const string TestingEnvironment = "Testing";

    private readonly Dictionary<string, string?> _settings;

    public UiAppFactory(bool cspReportOnly = false)
    {
        _settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Diagnostics:EnableUiProbePages"] = "true",
            ["Security:Csp:ReportOnly"] = cspReportOnly ? "true" : "false",

            // Hermetic: an OTLP endpoint inherited from the developer's or CI's environment must not export.
            [ObservabilityExtensions.OtlpEndpointConfigurationKey] = string.Empty,

            // Less test output; only the console provider is quietened.
            ["Logging:Console:LogLevel:Default"] = "Warning",
        };

        // Port 0: Kestrel picks a free port, so parallel runs and other local services cannot collide.
        UseKestrel(0);
    }

    /// <summary>The address Kestrel listens on, e.g. <c>http://127.0.0.1:54321</c>. Starts the server if needed.</summary>
    public Uri StartAndGetBaseAddress()
    {
        StartServer();

        var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not report a listening address.");

        return new Uri(address);
    }

    /// <summary>
    /// Simulates an outage (AC-08): stops Kestrel with an already cancelled token, so it does not drain and aborts
    /// every open connection, including the circuit's WebSocket. A graceful stop is not a valid simulation: the
    /// circuit could reconnect while the server waits for connections to close. Dispose the factory afterwards.
    /// </summary>
    public Task KillServerAsync() =>
        Services.GetRequiredService<IServer>().StopAsync(new CancellationToken(canceled: true));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestingEnvironment);

        // The app runs from the build output, not from a publish folder: framework, RCL and bundled CSS assets
        // are found through the static web assets manifest, which the host loads by itself only in Development.
        builder.UseStaticWebAssets();

        // UseSetting takes precedence over the app's JSON files and environment variables.
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
