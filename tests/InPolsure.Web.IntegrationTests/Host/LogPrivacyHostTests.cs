using System.Net;
using static InPolsure.Web.IntegrationTests.Host.HostResponse;

namespace InPolsure.Web.IntegrationTests.Host;

/// <summary>
/// ADR-0010 item 4 and NFR-042 on the real host (lab-01 T-01.8): query-string values never reach a log record.
/// The main risk is the hosting "Request starting ..." entry (<c>Microsoft.AspNetCore.Hosting.Diagnostics</c>,
/// Information), which contains the raw URL; the <c>appsettings*.json</c> keep that category at Warning in every
/// environment. Development matters most: it raises <c>Microsoft.AspNetCore</c> to Information, so without the
/// explicit category rule the entry would be written.
/// </summary>
public sealed class LogPrivacyHostTests
{
    private const string Secret = "secret-value";

    [Theory]
    [InlineData(InPolsureWebFactory.TestingEnvironment)]
    [InlineData("Development")]
    public async Task Get_with_token_in_query_string_logs_no_record_containing_the_value(string environmentName)
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = new InPolsureWebFactory(
            environmentName,
            configureTestServices: services => services.AddSingleton<ILoggerProvider>(logs));
        using var client = factory.CreateNonRedirectingClient();

        var (response, _) = await GetAsync(client, $"/?token={Secret}");
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Positive control: the provider is wired and records the app's logs (host startup at least).
        Assert.NotEmpty(logs.Entries);
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains(Secret, StringComparison.Ordinal));
    }
}
