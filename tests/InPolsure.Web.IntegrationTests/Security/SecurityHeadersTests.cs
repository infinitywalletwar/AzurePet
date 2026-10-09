using System.Net;
using InPolsure.Web.Security;
using Microsoft.Extensions.Options;
using static InPolsure.Web.IntegrationTests.Security.SecurityHeadersTestHost;

namespace InPolsure.Web.IntegrationTests.Security;

/// <summary>AC-06 on a minimal host (lab-01 T-01.3).</summary>
public sealed class SecurityHeadersTests
{
    // The exact policy of lab-01 §6.1 (ADR-0011 item 10, tightened). Kept literal on purpose.
    private const string ExpectedPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
        "font-src 'self'; base-uri 'self'; form-action 'self'; object-src 'none'; frame-ancestors 'none'";

    private const string Csp = "Content-Security-Policy";
    private const string CspReportOnly = "Content-Security-Policy-Report-Only";
    private const string Hsts = "Strict-Transport-Security";

    [Fact]
    public async Task Get_default_options_returns_enforced_csp_with_exact_policy()
    {
        await using var app = Build();

        using var response = await GetAsync(app, "/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([ExpectedPolicy], Values(response, Csp));
        Assert.Empty(Values(response, CspReportOnly));
    }

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("Referrer-Policy", "strict-origin-when-cross-origin")]
    [InlineData("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()")]
    [InlineData("Cross-Origin-Opener-Policy", "same-origin")]
    [InlineData("X-Frame-Options", "DENY")]
    public async Task Get_default_options_returns_header_with_expected_value(string name, string expected)
    {
        await using var app = Build();

        using var response = await GetAsync(app, "/");

        Assert.Equal([expected], Values(response, name));
    }

    [Fact]
    public async Task Get_hsts_disabled_by_default_returns_no_hsts_and_no_upgrade_directive()
    {
        await using var app = Build();

        using var response = await GetAsync(app, "/");

        Assert.Empty(Values(response, Hsts));
        Assert.Equal([ExpectedPolicy], Values(response, Csp));
    }

    [Fact]
    public async Task Get_hsts_explicitly_disabled_returns_no_hsts()
    {
        await using var app = Build(Settings(("Security:Hsts:Enabled", "false")));

        using var response = await GetAsync(app, "/");

        Assert.Empty(Values(response, Hsts));
        Assert.Equal([ExpectedPolicy], Values(response, Csp));
    }

    [Fact]
    public async Task Get_hsts_enabled_returns_one_year_hsts_and_upgrade_directive()
    {
        await using var app = Build(Settings(("Security:Hsts:Enabled", "true")));

        using var response = await GetAsync(app, "/");

        Assert.Equal(["max-age=31536000"], Values(response, Hsts));
        Assert.Equal([ExpectedPolicy + "; upgrade-insecure-requests"], Values(response, Csp));
    }

    [Fact]
    public async Task Get_hsts_enabled_with_custom_max_age_and_subdomains_returns_configured_hsts()
    {
        await using var app = Build(Settings(
            ("Security:Hsts:Enabled", "true"),
            ("Security:Hsts:MaxAgeSeconds", "63072000"),
            ("Security:Hsts:IncludeSubDomains", "true")));

        using var response = await GetAsync(app, "/");

        Assert.Equal(["max-age=63072000; includeSubDomains"], Values(response, Hsts));
    }

    [Fact]
    public async Task Get_report_only_returns_policy_under_report_only_header_name()
    {
        await using var app = Build(Settings(("Security:Csp:ReportOnly", "true")));

        using var response = await GetAsync(app, "/");

        Assert.Equal([ExpectedPolicy], Values(response, CspReportOnly));
        Assert.Empty(Values(response, Csp));
    }

    [Fact]
    public async Task Get_report_only_with_hsts_enabled_omits_upgrade_directive()
    {
        // Browsers ignore upgrade-insecure-requests in a report-only policy and log a CSP console warning,
        // which the UI tests would count as a violation.
        await using var app = Build(Settings(("Security:Csp:ReportOnly", "true"), ("Security:Hsts:Enabled", "true")));

        using var response = await GetAsync(app, "/");

        Assert.Equal([ExpectedPolicy], Values(response, CspReportOnly));
        Assert.Equal(["max-age=31536000"], Values(response, Hsts));
    }

    [Fact]
    public async Task Get_unmatched_path_returns_404_with_headers()
    {
        await using var app = Build();

        using var response = await GetAsync(app, "/missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal([ExpectedPolicy], Values(response, Csp));
        Assert.Equal(["nosniff"], Values(response, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task Get_response_started_by_body_write_returns_headers()
    {
        await using var app = Build(configurePipeline: a =>
        {
            a.UseInPolsureSecurityHeaders();
            a.MapGet("/stream", async (HttpContext context) =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(Html, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            });
        });

        using var response = await GetAsync(app, "/stream");

        Assert.Equal([ExpectedPolicy], Values(response, Csp));
        Assert.Equal(["same-origin"], Values(response, "Cross-Origin-Opener-Policy"));
    }

    [Fact]
    public async Task Get_downstream_frame_ancestors_csp_keeps_both_policies()
    {
        // Blazor adds "frame-ancestors 'none'" as its own CSP header on interactive endpoints.
        await using var app = Build(configurePipeline: a =>
        {
            a.UseInPolsureSecurityHeaders();
            a.MapGet("/interactive", (HttpContext context) =>
            {
                context.Response.Headers.Append(Csp, "frame-ancestors 'none'");
                return Results.Content(Html, "text/html");
            });
        });

        using var response = await GetAsync(app, "/interactive");

        var values = Values(response, Csp);
        Assert.Equal(2, values.Length);
        Assert.Contains("frame-ancestors 'none'", values);
        Assert.Contains(ExpectedPolicy, values);
    }

    [Fact]
    public async Task Get_downstream_x_frame_options_sameorigin_returns_deny()
    {
        // Antiforgery writes X-Frame-Options: SAMEORIGIN; the app-wide value must match frame-ancestors 'none'.
        await using var app = Build(configurePipeline: a =>
        {
            a.UseInPolsureSecurityHeaders();
            a.MapGet("/form", (HttpContext context) =>
            {
                context.Response.Headers.XFrameOptions = "SAMEORIGIN";
                return Results.Content(Html, "text/html");
            });
        });

        using var response = await GetAsync(app, "/form");

        Assert.Equal(["DENY"], Values(response, "X-Frame-Options"));
    }

    [Fact]
    public async Task Get_status_code_pages_reexecution_returns_each_header_once()
    {
        await using var app = Build(configurePipeline: a =>
        {
            a.UseStatusCodePagesWithReExecute("/not-found");
            a.UseInPolsureSecurityHeaders();
            a.MapGet("/not-found", () => Results.Content(Html, "text/html"));
        });

        using var response = await GetAsync(app, "/missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertEachHeaderOnce(response, ExpectedPolicy);
    }

    [Fact]
    public async Task Get_exception_handler_reexecution_returns_each_header_once()
    {
        await using var app = Build(
            Settings(("Security:Hsts:Enabled", "true")),
            a =>
            {
                a.UseExceptionHandler("/error");
                a.UseInPolsureSecurityHeaders();
                a.MapGet("/error", () => Results.Content(Html, "text/html", statusCode: StatusCodes.Status500InternalServerError));
                a.MapGet("/boom", (HttpContext context) =>
                {
                    context.Response.Headers.Append(Csp, "frame-ancestors 'none'");
                    throw new InvalidOperationException("Test failure.");
                });
            });

        using var response = await GetAsync(app, "/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertEachHeaderOnce(response, ExpectedPolicy + "; upgrade-insecure-requests");
        Assert.Equal(["max-age=31536000"], Values(response, Hsts));
    }

    [Fact]
    public async Task Start_hsts_max_age_below_one_year_throws_validation_exception()
    {
        await using var app = Build(Settings(("Security:Hsts:Enabled", "true"), ("Security:Hsts:MaxAgeSeconds", "86400")));

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => app.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(typeof(SecurityHstsOptions), exception.OptionsType);
        Assert.Contains("MaxAgeSeconds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_report_only_not_a_boolean_throws_at_startup()
    {
        await using var app = Build(Settings(("Security:Csp:ReportOnly", "sometimes")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(TestContext.Current.CancellationToken));
    }

    private static Dictionary<string, string?> Settings(params (string Key, string? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    private static void AssertEachHeaderOnce(HttpResponseMessage response, string expectedPolicy)
    {
        // The exception handler clears headers set before the failure, so only the app-wide policy remains.
        Assert.Equal([expectedPolicy], Values(response, Csp));
        Assert.Single(Values(response, "X-Content-Type-Options"));
        Assert.Single(Values(response, "Referrer-Policy"));
        Assert.Single(Values(response, "Permissions-Policy"));
        Assert.Single(Values(response, "Cross-Origin-Opener-Policy"));
        Assert.Single(Values(response, "X-Frame-Options"));
    }
}
