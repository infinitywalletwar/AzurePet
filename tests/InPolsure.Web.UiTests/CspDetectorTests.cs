using InPolsure.Web.UiTests.Fixtures;
using Microsoft.Playwright;

namespace InPolsure.Web.UiTests;

/// <summary>
/// Positive control for the CSP violation detectors of <see cref="BrowserSession"/> (lab-01 §6.5): an inline style,
/// which <c>style-src 'self'</c> forbids, must be seen by both the console listener and the
/// <c>securitypolicyviolation</c> listener, in enforced and in report-only mode. Without this, a broken detector
/// would let every "no CSP violations" assertion pass.
/// </summary>
[Collection(BrowserTestGroup.Name)]
public sealed class CspDetectorTests(UiTestFixture fixture)
{
    private const string InjectInlineStyle = """
        () => {
            const style = document.createElement("style");
            style.textContent = "body { outline: 1px solid red; }";
            document.head.appendChild(style);
        }
        """;

    [Fact]
    public async Task Inline_style_injected_on_root_is_reported_by_both_csp_detectors()
    {
        await using var session = await fixture.NewSessionAsync();
        await session.GotoAsync("/");
        await session.AssertNoCspViolationsAsync();

        await session.Page.RunAndWaitForConsoleMessageAsync(
            () => session.Page.EvaluateAsync(InjectInlineStyle),
            new PageRunAndWaitForConsoleMessageOptions
            {
                Predicate = message => message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase),
            });
        await session.Page.WaitForFunctionAsync("() => window.__cspViolations.length > 0");

        var events = await session.GetCspViolationEventsAsync();
        Assert.Contains(events, e => e.Contains("style-src", StringComparison.Ordinal));
        Assert.NotEmpty(session.CspConsoleMessages);
    }
}
