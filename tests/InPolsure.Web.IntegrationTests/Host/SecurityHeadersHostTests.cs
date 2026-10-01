using System.Net;
using static InPolsure.Web.IntegrationTests.Host.HostResponse;

namespace InPolsure.Web.IntegrationTests.Host;

/// <summary>AC-06 on the real host with its <c>appsettings*.json</c> (lab-01 T-01.8).</summary>
/// <remarks>
/// Blazor adds its own <c>Content-Security-Policy: frame-ancestors 'none'</c> header
/// (<c>ContentSecurityFrameAncestorsPolicy</c> in <c>Program.cs</c>) to every Razor component response, not only to
/// interactive pages, because any page can host interactive components. HTML pages therefore carry two enforced CSP
/// headers: the app policy and Blazor's. Browsers enforce both; they agree on <c>frame-ancestors 'none'</c>.
/// Responses that are not Razor components (static assets, health) carry only the app policy.
/// </remarks>
public sealed class SecurityHeadersHostTests
{
    // The exact policy of lab-01 §6.1 (ADR-0011 item 10, tightened). Kept literal on purpose.
    private const string Policy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
        "font-src 'self'; base-uri 'self'; form-action 'self'; object-src 'none'; frame-ancestors 'none'";

    // appsettings.json enables HSTS, which adds upgrade-insecure-requests to the enforced policy.
    private const string PolicyWithUpgrade = Policy + "; upgrade-insecure-requests";

    private const string BlazorFrameAncestorsPolicy = "frame-ancestors 'none'";

    private const string Csp = "Content-Security-Policy";
    private const string CspReportOnly = "Content-Security-Policy-Report-Only";
    private const string Hsts = "Strict-Transport-Security";

    [Theory]
    [InlineData("/")]
    [InlineData("/does-not-exist")]
    public async Task Get_html_page_returns_exact_enforced_csp(string path)
    {
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, path);
        using (response)
        {
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            AssertHtmlPagePolicies(response, PolicyWithUpgrade);
            Assert.Empty(Values(response, CspReportOnly));
        }
    }

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("Referrer-Policy", "strict-origin-when-cross-origin")]
    [InlineData("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()")]
    [InlineData("Cross-Origin-Opener-Policy", "same-origin")]
    [InlineData("X-Frame-Options", "DENY")]
    public async Task Get_root_returns_header_once_with_expected_value(string name, string expected)
    {
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, "/");
        using (response)
        {
            Assert.Equal([expected], Values(response, name));
        }
    }

    [Fact]
    public async Task Get_root_with_default_settings_returns_one_year_hsts_without_subdomains()
    {
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, "/");
        using (response)
        {
            Assert.Equal(["max-age=31536000"], Values(response, Hsts));
        }
    }

    [Fact]
    public async Task Get_root_in_development_returns_no_hsts_and_policy_without_upgrade()
    {
        await using var factory = new InPolsureWebFactory(environmentName: "Development");
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, "/");
        using (response)
        {
            Assert.Empty(Values(response, Hsts));
            AssertHtmlPagePolicies(response, Policy);
        }
    }

    [Fact]
    public async Task Get_root_with_hsts_disabled_returns_no_hsts()
    {
        await using var factory = new InPolsureWebFactory(settings: Settings(("Security:Hsts:Enabled", "false")));
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, "/");
        using (response)
        {
            Assert.Empty(Values(response, Hsts));
            AssertHtmlPagePolicies(response, Policy);
        }
    }

    [Fact]
    public async Task Get_root_with_report_only_returns_policy_under_report_only_header_only()
    {
        await using var factory = new InPolsureWebFactory(settings: Settings(("Security:Csp:ReportOnly", "true")));
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, "/");
        using (response)
        {
            Assert.Equal([Policy], Values(response, CspReportOnly));

            // Only Blazor's frame-ancestors header stays enforced; the app policy is not enforced.
            Assert.Equal([BlazorFrameAncestorsPolicy], Values(response, Csp));
        }
    }

    [Fact]
    public async Task Get_interactive_probe_page_returns_app_policy_and_blazor_frame_ancestors_policy()
    {
        await using var factory = new InPolsureWebFactory(settings: Settings(("Diagnostics:EnableUiProbePages", "true")));
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, "/_probe/interactive");
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertHtmlPagePolicies(response, PolicyWithUpgrade);
            Assert.Equal(["DENY"], Values(response, "X-Frame-Options"));
        }
    }

    [Fact]
    public async Task Get_static_asset_returns_security_headers()
    {
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, "/favicon.svg");
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal([PolicyWithUpgrade], Values(response, Csp));
            Assert.Equal(["nosniff"], Values(response, "X-Content-Type-Options"));
        }
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Get_health_endpoint_returns_app_policy_only(string path)
    {
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, path);
        using (response)
        {
            Assert.Equal([PolicyWithUpgrade], Values(response, Csp));
            Assert.Equal(["max-age=31536000"], Values(response, Hsts));
        }
    }

    /// <summary>Exactly two enforced CSP headers: the exact app policy once, and Blazor's frame-ancestors policy.</summary>
    private static void AssertHtmlPagePolicies(HttpResponseMessage response, string appPolicy)
    {
        var values = Values(response, Csp);
        Assert.Equal(2, values.Length);
        Assert.Single(values, v => string.Equals(v, appPolicy, StringComparison.Ordinal));
        Assert.Single(values, v => string.Equals(v, BlazorFrameAncestorsPolicy, StringComparison.Ordinal));
    }

    private static Dictionary<string, string?> Settings(params (string Key, string? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}
