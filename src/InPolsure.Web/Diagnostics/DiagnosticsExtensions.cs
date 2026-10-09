namespace InPolsure.Web.Diagnostics;

/// <summary>
/// Wiring for diagnostic features (lab-01 §6.4).
/// </summary>
public static class DiagnosticsExtensions
{
    /// <summary>
    /// Binds and validates <see cref="DiagnosticsOptions"/> (<c>Diagnostics</c>). The probe pages read these
    /// options; without this call they fall back to the defaults and stay disabled.
    /// </summary>
    public static IServiceCollection AddInPolsureDiagnostics(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DiagnosticsOptions>()
            .Bind(configuration.GetSection(DiagnosticsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
