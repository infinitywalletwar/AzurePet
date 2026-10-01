using System.Net;
using static InPolsure.Web.IntegrationTests.Host.HostResponse;

namespace InPolsure.Web.IntegrationTests.Host;

/// <summary>AC-10 on the real host (lab-01 T-01.8).</summary>
public sealed class ProbePageHostTests
{
    private const string ProbePath = "/_probe/interactive";

    [Fact]
    public async Task Get_probe_page_with_default_settings_returns_404_not_found_page_without_circuit()
    {
        // appsettings.json: Diagnostics:EnableUiProbePages = false.
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();

        var (response, body) = await GetAsync(client, ProbePath);
        using (response)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("<h1>Page not found</h1>", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Interactive probe", body, StringComparison.Ordinal);

            // No interactive component is rendered, so no circuit can be started from this response.
            Assert.DoesNotContain(InteractiveComponentMarker, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Get_probe_page_explicitly_disabled_in_development_returns_404()
    {
        await using var factory = new InPolsureWebFactory(
            environmentName: "Development",
            settings: new Dictionary<string, string?> { ["Diagnostics:EnableUiProbePages"] = "false" });
        using var client = factory.CreateNonRedirectingClient();

        var (response, body) = await GetAsync(client, ProbePath);
        using (response)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain(InteractiveComponentMarker, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Get_probe_page_enabled_returns_200_with_prerendered_interactive_page()
    {
        await using var factory = new InPolsureWebFactory(
            settings: new Dictionary<string, string?> { ["Diagnostics:EnableUiProbePages"] = "true" });
        using var client = factory.CreateNonRedirectingClient();

        var (response, body) = await GetAsync(client, ProbePath);
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // CSS isolation adds a scope attribute to the element (<h1 b-xxxx>).
            Assert.Matches("<h1[^>]*>Interactive probe</h1>", body);
            Assert.Contains(InteractiveComponentMarker, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Get_probe_page_in_development_is_enabled_by_appsettings_development()
    {
        // docs/DELIVERY.md §10.2: Diagnostics:EnableUiProbePages is true locally (Development).
        await using var factory = new InPolsureWebFactory(environmentName: "Development");
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, ProbePath);
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
