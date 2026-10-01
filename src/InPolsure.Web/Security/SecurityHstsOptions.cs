using System.ComponentModel.DataAnnotations;

namespace InPolsure.Web.Security;

/// <summary>
/// HTTP Strict Transport Security settings, bound from <c>Security:Hsts</c> (lab-01 §6.1).
/// </summary>
public sealed class SecurityHstsOptions
{
    /// <summary>Configuration section path.</summary>
    public const string SectionName = "Security:Hsts";

    /// <summary>One year in seconds: the minimum <c>max-age</c> accepted (lab-01 §6.1).</summary>
    public const int OneYearInSeconds = 31_536_000;

    /// <summary>
    /// When <see langword="true"/>, every response carries <c>Strict-Transport-Security</c> and the CSP gains
    /// <c>upgrade-insecure-requests</c>. Default <see langword="false"/>; set to <see langword="true"/> outside Development.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// <c>max-age</c> in seconds. At least one year. Validated at startup even when <see cref="Enabled"/> is
    /// <see langword="false"/> (intentional fail-fast: a bad value is caught before HSTS is switched on).
    /// </summary>
    [Range(OneYearInSeconds, int.MaxValue, ErrorMessage = "Security:Hsts:MaxAgeSeconds must be at least 31536000 (one year).")]
    public int MaxAgeSeconds { get; set; } = OneYearInSeconds;

    /// <summary>
    /// Adds <c>includeSubDomains</c>. Default <see langword="false"/>: tenant hosts share a parent domain
    /// (ADR-0016), so widening HSTS to every subdomain is an explicit decision per environment.
    /// </summary>
    public bool IncludeSubDomains { get; set; }
}
