namespace InPolsure.Web.Health;

/// <summary>
/// Paths and tags of the health endpoints (lab-01 §6.2).
/// </summary>
public static class HealthEndpoints
{
    /// <summary>Liveness probe: runs no checks, answers while the process is alive.</summary>
    public const string LivePath = "/health/live";

    /// <summary>Readiness probe: runs only checks tagged <see cref="ReadyTag"/>.</summary>
    public const string ReadyPath = "/health/ready";

    /// <summary>Tag for health checks that must pass before the app receives traffic.</summary>
    public const string ReadyTag = "ready";
}
