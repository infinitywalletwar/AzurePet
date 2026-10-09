namespace InPolsure.Web.Security;

/// <summary>
/// Content Security Policy settings, bound from <c>Security:Csp</c> (lab-01 §6.1).
/// The policy itself is fixed in code (ADR-0011 item 10); only the delivery mode is configurable.
/// </summary>
public sealed class CspOptions
{
    /// <summary>Configuration section path.</summary>
    public const string SectionName = "Security:Csp";

    /// <summary>
    /// When <see langword="true"/>, the policy is sent as <c>Content-Security-Policy-Report-Only</c>
    /// (violations are reported in the browser console but not blocked). Default: enforced.
    /// </summary>
    public bool ReportOnly { get; set; }
}
