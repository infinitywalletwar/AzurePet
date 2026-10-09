using Microsoft.Playwright;

namespace InPolsure.Web.UiTests.Fixtures;

/// <summary>
/// One running app and one browser per test collection; every test gets its own <see cref="BrowserSession"/>
/// (a new browser context, so cookies, storage and offline state never leak between tests).
/// </summary>
/// <remarks>
/// Environment variables (all optional):
/// <list type="bullet">
/// <item><c>UITESTS_BROWSER</c>: <c>chromium</c> (default, the only browser in CI), <c>firefox</c> or <c>webkit</c>.
/// Install the browser first with <c>playwright.ps1 install</c>.</item>
/// <item><c>UITESTS_CSP_REPORT_ONLY</c>: <c>true</c> runs the app with <c>Security:Csp:ReportOnly=true</c>
/// (ADR-0017 item 5: collect violations of new UI before enforcing). The violation detectors work in both modes;
/// the default and CI run enforced.</item>
/// <item><c>UITESTS_HEADED</c>: <c>true</c> shows the browser window (local debugging).</item>
/// </list>
/// </remarks>
public sealed class UiTestFixture : IAsyncLifetime
{
    public const string BrowserVariable = "UITESTS_BROWSER";
    public const string CspReportOnlyVariable = "UITESTS_CSP_REPORT_ONLY";
    public const string HeadedVariable = "UITESTS_HEADED";

    /// <summary>Upper bound for every Playwright action and expect-style wait; generous for slow CI runners.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private UiAppFactory? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public Uri BaseAddress { get; private set; } = null!;

    public bool CspReportOnly { get; } = IsTrue(CspReportOnlyVariable);

    public async ValueTask InitializeAsync()
    {
        _app = new UiAppFactory(CspReportOnly);
        BaseAddress = _app.StartAndGetBaseAddress();

        Assertions.SetDefaultExpectTimeout((float)Timeout.TotalMilliseconds);
        _playwright = await Playwright.CreateAsync();
        _browser = await LaunchBrowserAsync(_playwright);
    }

    /// <summary>Opens a new browser context and page with the violation detectors attached.</summary>
    /// <param name="viewport">Viewport size; Playwright's default (1280 x 720) when null.</param>
    /// <param name="baseAddress">The app to browse; the shared app when null.</param>
    public Task<BrowserSession> NewSessionAsync(ViewportSize? viewport = null, Uri? baseAddress = null) =>
        BrowserSession.CreateAsync(
            _browser ?? throw new InvalidOperationException("The fixture is not initialised."),
            baseAddress ?? BaseAddress,
            viewport);

    /// <summary>
    /// An extra, not yet started app with the same settings as the shared one, for a test that stops its server
    /// (AC-08). The caller disposes it.
    /// </summary>
    internal UiAppFactory CreateDedicatedApp() => new(CspReportOnly);

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private static Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
    {
        var name = Environment.GetEnvironmentVariable(BrowserVariable);
        var browserType = string.IsNullOrWhiteSpace(name) ? "chromium" : name.Trim().ToLowerInvariant();
        var options = new BrowserTypeLaunchOptions
        {
            Headless = !IsTrue(HeadedVariable),
            Timeout = (float)Timeout.TotalMilliseconds,
        };

        return browserType switch
        {
            "chromium" => playwright.Chromium.LaunchAsync(options),
            "firefox" => playwright.Firefox.LaunchAsync(options),
            "webkit" => playwright.Webkit.LaunchAsync(options),
            _ => throw new InvalidOperationException(
                $"{BrowserVariable}='{name}' is not supported. Use chromium, firefox or webkit."),
        };
    }

    private static bool IsTrue(string variable) =>
        bool.TryParse(Environment.GetEnvironmentVariable(variable), out var value) && value;
}
