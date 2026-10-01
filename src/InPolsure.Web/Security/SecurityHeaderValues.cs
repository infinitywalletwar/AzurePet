namespace InPolsure.Web.Security;

/// <summary>
/// Header names and values sent on every response (lab-01 §6.1, ADR-0011 item 10, NFR-026).
/// Computed once at startup from validated options.
/// </summary>
internal sealed class SecurityHeaderValues
{
    public const string ContentSecurityPolicyHeader = "Content-Security-Policy";
    public const string ContentSecurityPolicyReportOnlyHeader = "Content-Security-Policy-Report-Only";
    public const string ContentTypeOptionsHeader = "X-Content-Type-Options";
    public const string ReferrerPolicyHeader = "Referrer-Policy";
    public const string PermissionsPolicyHeader = "Permissions-Policy";
    public const string CrossOriginOpenerPolicyHeader = "Cross-Origin-Opener-Policy";
    public const string FrameOptionsHeader = "X-Frame-Options";
    public const string StrictTransportSecurityHeader = "Strict-Transport-Security";

    /// <summary>The CSP of lab-01 §6.1 (ADR-0011 item 10, tightened). Changing it needs the architect.</summary>
    public const string BasePolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
        "font-src 'self'; base-uri 'self'; form-action 'self'; object-src 'none'; frame-ancestors 'none'";

    public const string UpgradeInsecureRequestsDirective = "upgrade-insecure-requests";

    public const string ContentTypeOptions = "nosniff";
    public const string ReferrerPolicy = "strict-origin-when-cross-origin";
    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=()";
    public const string CrossOriginOpenerPolicy = "same-origin";

    /// <summary>Matches CSP <c>frame-ancestors 'none'</c> for browsers and scanners that only read X-Frame-Options.</summary>
    public const string FrameOptions = "DENY";

    private SecurityHeaderValues(string cspHeaderName, string csp, string? strictTransportSecurity)
    {
        CspHeaderName = cspHeaderName;
        Csp = csp;
        StrictTransportSecurity = strictTransportSecurity;
    }

    public string CspHeaderName { get; }

    public string Csp { get; }

    /// <summary><see langword="null"/> when HSTS is disabled.</summary>
    public string? StrictTransportSecurity { get; }

    public static SecurityHeaderValues Create(CspOptions csp, SecurityHstsOptions hsts)
    {
        ArgumentNullException.ThrowIfNull(csp);
        ArgumentNullException.ThrowIfNull(hsts);

        // upgrade-insecure-requests is ignored (with a console warning) in a report-only policy,
        // so it is only added to the enforced header.
        var policy = hsts.Enabled && !csp.ReportOnly
            ? $"{BasePolicy}; {UpgradeInsecureRequestsDirective}"
            : BasePolicy;

        var headerName = csp.ReportOnly ? ContentSecurityPolicyReportOnlyHeader : ContentSecurityPolicyHeader;

        string? sts = null;
        if (hsts.Enabled)
        {
            sts = hsts.IncludeSubDomains
                ? $"max-age={hsts.MaxAgeSeconds}; includeSubDomains"
                : $"max-age={hsts.MaxAgeSeconds}";
        }

        return new SecurityHeaderValues(headerName, policy, sts);
    }
}
