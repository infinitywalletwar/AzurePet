using InPolsure.Web.Observability;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace InPolsure.Web.IntegrationTests.Host;

/// <summary>
/// The real host (<c>Program.cs</c> and its <c>appsettings*.json</c>) under <see cref="WebApplicationFactory{TEntryPoint}"/>
/// (lab-01 §6.5, T-01.8). Configuration overrides are applied with <c>UseSetting</c>, which takes precedence over
/// the app's JSON files and environment variables and is visible while <c>Program.cs</c> registers services.
/// </summary>
internal sealed class InPolsureWebFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Environment used by default: not Development, so the host runs with the production-like settings of
    /// <c>appsettings.json</c> (HSTS on, probe pages off, exception handler, JSON console).
    /// </summary>
    public const string TestingEnvironment = "Testing";

    private readonly string _environmentName;
    private readonly Dictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureTestServices;

    public InPolsureWebFactory(
        string environmentName = TestingEnvironment,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureTestServices = null)
    {
        _environmentName = environmentName;
        _configureTestServices = configureTestServices;

        // Hermetic by default: an OTLP endpoint inherited from the developer's or CI's environment must not
        // make tests export telemetry. A test can still set it through the settings.
        _settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [ObservabilityExtensions.OtlpEndpointConfigurationKey] = string.Empty,

            // Less test output: only the console provider is quietened. Other providers (OpenTelemetry, the
            // log-content test's capturing provider) keep the app's Logging:LogLevel rules.
            ["Logging:Console:LogLevel:Default"] = "Warning",
        };

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }
    }

    /// <summary>A client that does not follow redirects, so status codes are asserted as the host sends them.</summary>
    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        if (_configureTestServices is not null)
        {
            builder.ConfigureTestServices(_configureTestServices);
        }
    }
}
