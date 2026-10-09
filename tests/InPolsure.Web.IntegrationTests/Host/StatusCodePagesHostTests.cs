using System.Net;
using InPolsure.Web.Health;
using static InPolsure.Web.IntegrationTests.Host.HostResponse;

namespace InPolsure.Web.IntegrationTests.Host;

/// <summary>
/// Status-code re-execution on the real host (lab-01 §6.2, review item C-01): only a GET or HEAD 404 is re-executed
/// to the HTML not-found page; other methods and statuses keep the response the endpoint produced. Before the fix,
/// a POST with a body was re-executed as POST to the Razor not-found endpoint, which answered 400.
/// </summary>
public sealed class StatusCodePagesHostTests
{
    [Theory]
    [InlineData(HealthEndpoints.LivePath)]
    [InlineData(HealthEndpoints.ReadyPath)]
    public async Task Post_health_endpoint_returns_405_with_allow_get_head(string path)
    {
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();
        using var content = new StringContent(string.Empty);

        using var response = await client.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["GET", "HEAD"], response.Content.Headers.Allow.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Page not found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_unknown_path_returns_404_with_not_found_page()
    {
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();

        var (response, body) = await GetAsync(client, "/no-such-page");
        using (response)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<h1>Page not found</h1>", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Head_unknown_path_returns_404()
    {
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, new Uri("/no-such-page", UriKind.Relative));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
