using System.Net;
using InPolsure.Web.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using static InPolsure.Web.IntegrationTests.Host.HostResponse;

namespace InPolsure.Web.IntegrationTests.Host;

/// <summary>AC-04 (home page, Static SSR), not-found and error pages on the real host (lab-01 T-01.8).</summary>
public sealed class PagesTests
{
    [Fact]
    public async Task Get_root_returns_static_ssr_html_page_with_english_lang_and_title()
    {
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();

        var (response, body) = await GetAsync(client, "/");
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<html lang=\"en\">", body, StringComparison.Ordinal);
            Assert.Matches("<title>[^<]*InPolsure[^<]*</title>", body);

            // Static SSR: no component is rendered with an interactive render mode, so blazor.web.js has
            // nothing to start a circuit for (the browser-level check is in the UI tests, T-01.9).
            Assert.DoesNotContain(InteractiveComponentMarker, body, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("/does-not-exist")]
    [InlineData("/does/not/exist")]
    public async Task Get_unknown_path_returns_404_with_not_found_page(string path)
    {
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();

        var (response, body) = await GetAsync(client, path);
        using (response)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<h1>Page not found</h1>", body, StringComparison.Ordinal);
            Assert.Contains("<html lang=\"en\">", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Get_page_that_throws_outside_development_returns_500_error_page_without_details()
    {
        // No production code throws on purpose; the test replaces a dependency of the probe page with one that
        // fails while the page renders. The exception handler re-executes /Error.
        await using var factory = new InPolsureWebFactory(
            settings: new Dictionary<string, string?> { ["Diagnostics:EnableUiProbePages"] = "true" },
            configureTestServices: services =>
                services.Replace(ServiceDescriptor.Singleton<IOptions<DiagnosticsOptions>>(new ThrowingDiagnosticsOptions())));
        using var client = factory.CreateNonRedirectingClient();

        var (response, body) = await GetAsync(client, "/_probe/interactive");
        using (response)
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("<h1>Something went wrong</h1>", body, StringComparison.Ordinal);
            Assert.DoesNotContain(ThrowingDiagnosticsOptions.Message, body, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(ThrowingDiagnosticsOptions), body, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        }
    }

    private sealed class ThrowingDiagnosticsOptions : IOptions<DiagnosticsOptions>
    {
        public const string Message = "Simulated failure 7f3c9e: internal detail that must not reach the client.";

        public DiagnosticsOptions Value => throw new InvalidOperationException(Message);
    }
}
