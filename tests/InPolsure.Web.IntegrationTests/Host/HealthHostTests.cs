using System.Net;
using InPolsure.Web.Health;
using static InPolsure.Web.IntegrationTests.Host.HostResponse;

namespace InPolsure.Web.IntegrationTests.Host;

/// <summary>AC-05 on the real host (lab-01 T-01.8).</summary>
public sealed class HealthHostTests
{
    [Theory]
    [InlineData(HealthEndpoints.LivePath)]
    [InlineData(HealthEndpoints.ReadyPath)]
    public async Task Get_health_endpoint_anonymously_returns_200_healthy_no_store(string path)
    {
        await using var factory = new InPolsureWebFactory();

        // No credentials of any kind are sent.
        using var client = factory.CreateNonRedirectingClient();

        var (response, body) = await GetAsync(client, path);
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", body);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.NotNull(response.Headers.CacheControl);
            Assert.True(response.Headers.CacheControl.NoStore);
        }
    }

    [Theory]
    [InlineData(HealthEndpoints.LivePath)]
    [InlineData(HealthEndpoints.ReadyPath)]
    public async Task Head_health_endpoint_returns_200(string path)
    {
        await using var factory = new InPolsureWebFactory();
        using var client = factory.CreateNonRedirectingClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, new Uri(path, UriKind.Relative));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_health_endpoint_in_development_returns_200()
    {
        await using var factory = new InPolsureWebFactory(environmentName: "Development");
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, HealthEndpoints.LivePath);
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
