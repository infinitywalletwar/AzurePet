using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace InPolsure.Web.IntegrationTests;

// Wave 0 placeholder: proves the host starts under WebApplicationFactory.
// Full-host tests for the lab acceptance criteria live in Host/ (T-01.8).
public sealed class PlaceholderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlaceholderTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Get_root_returns_ok()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
