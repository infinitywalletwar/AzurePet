namespace InPolsure.Web.IntegrationTests.Host;

/// <summary>Request and header helpers for the full-host tests.</summary>
internal static class HostResponse
{
    /// <summary>Marker that Blazor writes for every component rendered with an interactive render mode.</summary>
    public const string InteractiveComponentMarker = "<!--Blazor:";

    public static async Task<(HttpResponseMessage Response, string Body)> GetAsync(HttpClient client, string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (response, body);
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
