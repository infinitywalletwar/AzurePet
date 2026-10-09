using System.Collections.Concurrent;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace InPolsure.Web.UiTests.Fixtures;

/// <summary>
/// A browser context with one page and the detectors every browser test needs (lab-01 §6.5): CSP violations
/// (console messages and <c>securitypolicyviolation</c> events), WebSockets and Blazor circuit requests, and axe.
/// </summary>
public sealed class BrowserSession : IAsyncDisposable
{
    /// <summary>Path segment of the Blazor Server hub (negotiate and WebSocket).</summary>
    public const string BlazorHubPath = "/_blazor";

    /// <summary>WCAG 2.1 A and AA (lab-01 §6.5, NFR-080).</summary>
    public static readonly IReadOnlyList<string> AxeTags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];

    // Runs in every document before any page script, so violations caused by the first parsed element are seen.
    // The event fires in enforced and in report-only mode.
    private const string CspListenerScript = """
        window.__cspViolations = [];
        document.addEventListener("securitypolicyviolation", (e) => {
            window.__cspViolations.push(
                `${e.disposition} ${e.effectiveDirective}: blocked '${e.blockedURI}' at ${e.sourceFile}:${e.lineNumber}`);
        }, true);
        """;

    private readonly ConcurrentQueue<string> _cspConsoleMessages = new();
    private readonly ConcurrentQueue<string> _webSocketUrls = new();
    private readonly ConcurrentQueue<string> _blazorHubRequests = new();

    private BrowserSession(IBrowserContext context, IPage page)
    {
        Context = context;
        Page = page;
    }

    public IBrowserContext Context { get; }

    public IPage Page { get; }

    /// <summary>URLs of every WebSocket the page opened.</summary>
    public IReadOnlyCollection<string> WebSocketUrls => _webSocketUrls.ToArray();

    /// <summary>URLs of every HTTP request to the Blazor hub (negotiate, long polling).</summary>
    public IReadOnlyCollection<string> BlazorHubRequests => _blazorHubRequests.ToArray();

    internal static async Task<BrowserSession> CreateAsync(IBrowser browser, Uri baseAddress, ViewportSize? viewport)
    {
        var options = new BrowserNewContextOptions
        {
            BaseURL = baseAddress.ToString(),
            Locale = "en-US",
        };
        if (viewport is not null)
        {
            options.ViewportSize = viewport;
        }

        var context = await browser.NewContextAsync(options);
        context.SetDefaultTimeout((float)UiTestFixture.Timeout.TotalMilliseconds);
        await context.AddInitScriptAsync(CspListenerScript);

        var page = await context.NewPageAsync();
        var session = new BrowserSession(context, page);

        page.Console += (_, message) =>
        {
            if (message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
            {
                session._cspConsoleMessages.Enqueue(message.Text);
            }
        };
        page.WebSocket += (_, webSocket) => session._webSocketUrls.Enqueue(webSocket.Url);
        page.Request += (_, request) =>
        {
            if (new Uri(request.Url).AbsolutePath.StartsWith(BlazorHubPath, StringComparison.Ordinal))
            {
                session._blazorHubRequests.Enqueue(request.Url);
            }
        };

        return session;
    }

    /// <summary>Navigates and waits until the network is idle, so late script activity is included.</summary>
    public async Task<IResponse> GotoAsync(string path)
    {
        var response = await Page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        return response ?? throw new InvalidOperationException($"No response for '{path}'.");
    }

    /// <summary>Waits until blazor.web.js has loaded and started (it defines <c>window.Blazor</c>).</summary>
    public Task WaitForBlazorScriptAsync() =>
        Page.WaitForFunctionAsync("() => typeof window.Blazor === 'object' && window.Blazor !== null");

    /// <summary>Console messages that mention "Content Security Policy", seen in this session.</summary>
    public IReadOnlyCollection<string> CspConsoleMessages => _cspConsoleMessages.ToArray();

    /// <summary><c>securitypolicyviolation</c> events of the current document.</summary>
    public async Task<IReadOnlyList<string>> GetCspViolationEventsAsync() =>
        await Page.EvaluateAsync<string[]>("() => window.__cspViolations ?? []");

    /// <summary>CSP violations of the current document plus every CSP console message seen in this session.</summary>
    public async Task<IReadOnlyList<string>> GetCspViolationsAsync()
    {
        var events = await GetCspViolationEventsAsync();
        return [.. events, .. _cspConsoleMessages];
    }

    public async Task AssertNoCspViolationsAsync()
    {
        var violations = await GetCspViolationsAsync();
        Assert.True(violations.Count == 0, "CSP violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Runs axe with the WCAG 2.1 A/AA rules on the current page and fails on any violation. CSP violations of the
    /// app are asserted first; those raised while axe runs are then discarded, because axe-core itself injects
    /// inline <c>style</c> elements (test tooling, not app markup).
    /// </summary>
    public async Task AssertNoAxeViolationsAsync()
    {
        await AssertNoCspViolationsAsync();

        var result = await Page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. AxeTags] },
        });

        await Page.EvaluateAsync("() => { window.__cspViolations = []; }");
        _cspConsoleMessages.Clear();

        var violations = result.Violations
            .Select(v => $"{v.Id} ({v.Impact}): {v.Help} -> {string.Join(" | ", v.Nodes.Select(n => n.Html))}")
            .ToArray();
        Assert.True(violations.Length == 0, "axe violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    public async ValueTask DisposeAsync() => await Context.DisposeAsync();
}
