namespace InPolsure.Web.Observability;

/// <summary>
/// Decides which incoming requests produce a server span (lab-01 §6.3).
/// </summary>
/// <remarks>
/// The ASP.NET Core instrumentation runs this filter when the request activity starts, which
/// is before routing, so endpoint metadata (for example from <c>MapStaticAssets</c>) is not
/// available yet. Detection is therefore path-based:
/// <list type="bullet">
/// <item><description>Health probes: the path starts with the <c>/health</c> segment.</description></item>
/// <item><description>Framework and library assets: the path starts with <c>/_framework</c> or <c>/_content</c>.</description></item>
/// <item><description>Other static files: the last path segment has a file extension
/// (<c>/app.css</c>, <c>/favicon.ico</c>, fingerprinted <c>/app.abc123.css</c>).</description></item>
/// </list>
/// Consequence: application routes must not end in a segment with a dot, or they will not be
/// traced. This filter affects tracing only: health endpoints suppress their own HTTP metrics
/// (<c>DisableHttpMetrics()</c> in T-01.2), while static-asset requests still appear in the
/// request count and duration metrics.
/// </remarks>
internal static class TraceRequestFilter
{
    private static readonly PathString[] _excludedPrefixes =
    [
        new("/health"),
        new("/_framework"),
        new("/_content"),
    ];

    /// <summary>Returns <see langword="true"/> when the request should be traced.</summary>
    public static bool ShouldTrace(HttpContext context)
    {
        var path = context.Request.Path;

        foreach (var prefix in _excludedPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return !Path.HasExtension(path.Value);
    }
}
