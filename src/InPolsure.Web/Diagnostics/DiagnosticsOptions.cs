namespace InPolsure.Web.Diagnostics;

/// <summary>
/// Diagnostic switches, bound from <c>Diagnostics</c> (lab-01 §6.4).
/// </summary>
public sealed class DiagnosticsOptions
{
    /// <summary>Configuration section path.</summary>
    public const string SectionName = "Diagnostics";

    /// <summary>
    /// When <see langword="true"/>, the UI probe pages (<c>/_probe/*</c>, used by the browser tests for the
    /// CSP check of ADR-0017 item 5) are served. Default <see langword="false"/>: they answer 404.
    /// </summary>
    public bool EnableUiProbePages { get; set; }
}
