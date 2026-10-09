using InPolsure.Web.Security;
using Microsoft.AspNetCore.TestHost;

namespace InPolsure.Web.IntegrationTests.Security;

/// <summary>
/// Minimal host for the security headers feature (lab-01 §6.5): only the feature under test is wired,
/// configuration comes only from the given settings (no appsettings or environment variables).
/// </summary>
internal static class SecurityHeadersTestHost
{
    public const string Html = "<!DOCTYPE html><html lang=\"en\"><head><title>t</title></head><body></body></html>";

    public static WebApplication Build(IReadOnlyDictionary<string, string?>? settings = null, Action<WebApplication>? configurePipeline = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddInPolsureSecurityHeaders(builder.Configuration);

        var app = builder.Build();
        if (configurePipeline is null)
        {
            app.UseInPolsureSecurityHeaders();
            app.MapGet("/", () => Results.Content(Html, "text/html"));
        }
        else
        {
            configurePipeline(app);
        }

        return app;
    }

    public static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path)
    {
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        return await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
    }

    /// <summary>All values of a response header (each header line is one value), or empty if absent.</summary>
    public static string[] Values(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return [.. values];
        }

        return response.Content.Headers.TryGetValues(name, out var contentValues) ? [.. contentValues] : [];
    }
}
