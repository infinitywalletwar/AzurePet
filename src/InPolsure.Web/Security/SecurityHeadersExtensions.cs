namespace InPolsure.Web.Security;

/// <summary>
/// Wiring for security headers and the Content Security Policy (lab-01 §6.1, NFR-026).
/// </summary>
public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Binds and validates <see cref="CspOptions"/> (<c>Security:Csp</c>) and <see cref="SecurityHstsOptions"/>
    /// (<c>Security:Hsts</c>). Invalid values stop the host at startup.
    /// </summary>
    public static IServiceCollection AddInPolsureSecurityHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CspOptions>()
            .Bind(configuration.GetSection(CspOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SecurityHstsOptions>()
            .Bind(configuration.GetSection(SecurityHstsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Adds the security headers to every response. Place it after the exception handler and before static
    /// assets so error pages and static files carry the headers too. It also sends HSTS when enabled:
    /// do not call <c>UseHsts</c> or <c>UseHttpsRedirection</c> (TLS ends at the ingress, ADR-0004).
    /// </summary>
    public static IApplicationBuilder UseInPolsureSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
