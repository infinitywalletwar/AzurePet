using System.Net;
using InPolsure.Web.UiTests.Fixtures;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace InPolsure.Web.UiTests;

/// <summary>
/// AC-07 on the Interactive Server probe page (lab-01 §6.4, ADR-0017 item 5): a circuit starts, the button and
/// QuickGrid sorting work, and nothing violates the CSP or WCAG 2.1 A/AA.
/// </summary>
[Collection(BrowserTestGroup.Name)]
public sealed class InteractiveProbeTests(UiTestFixture fixture)
{
    public const string ProbePath = "/_probe/interactive";

    [Fact]
    public async Task Get_probe_page_establishes_circuit_without_csp_or_axe_violations()
    {
        await using var session = await fixture.NewSessionAsync();

        var webSocket = await session.Page.RunAndWaitForWebSocketAsync(
            () => session.GotoAsync(ProbePath),
            new PageRunAndWaitForWebSocketOptions { Predicate = ws => IsBlazorHub(ws.Url) });
        await WaitForInteractiveAsync(session.Page);

        Assert.False(webSocket.IsClosed);
        await session.AssertNoCspViolationsAsync();
        await session.AssertNoAxeViolationsAsync();
    }

    [Fact]
    public async Task Click_increment_button_updates_click_count()
    {
        await using var session = await fixture.NewSessionAsync();
        await OpenInteractiveProbeAsync(session);
        var count = session.Page.Locator("#probe-click-count");
        await Expect(count).ToHaveTextAsync("Click count: 0");

        var button = session.Page.GetByRole(AriaRole.Button, new() { Name = "Increment" });
        await button.ClickAsync();
        await Expect(count).ToHaveTextAsync("Click count: 1");
        await button.ClickAsync();
        await Expect(count).ToHaveTextAsync("Click count: 2");

        await session.AssertNoCspViolationsAsync();
    }

    [Fact]
    public async Task Click_sortable_column_header_toggles_row_order_and_aria_sort()
    {
        await using var session = await fixture.NewSessionAsync();
        await OpenInteractiveProbeAsync(session);
        var page = session.Page;
        var nameHeader = page.Locator(".probe-grid thead th").Filter(new() { HasText = "Name" });
        var names = page.Locator(".probe-grid tbody tr td:first-child");

        // Default sort column, ascending.
        await Expect(nameHeader).ToHaveAttributeAsync("aria-sort", "ascending");
        await Expect(names).ToHaveTextAsync(["Alpha", "Bravo", "Charlie", "Delta"]);

        await nameHeader.GetByRole(AriaRole.Button).ClickAsync();
        await Expect(nameHeader).ToHaveAttributeAsync("aria-sort", "descending");
        await Expect(names).ToHaveTextAsync(["Delta", "Charlie", "Bravo", "Alpha"]);

        await nameHeader.GetByRole(AriaRole.Button).ClickAsync();
        await Expect(nameHeader).ToHaveAttributeAsync("aria-sort", "ascending");
        await Expect(names).ToHaveTextAsync(["Alpha", "Bravo", "Charlie", "Delta"]);

        await session.AssertNoCspViolationsAsync();
        await session.AssertNoAxeViolationsAsync();
    }

    /// <summary>Opens the probe page and waits until its circuit is up and the page renders interactively.</summary>
    internal static async Task OpenInteractiveProbeAsync(BrowserSession session)
    {
        var response = await session.GotoAsync(ProbePath);
        Assert.Equal((int)HttpStatusCode.OK, response.Status);
        await WaitForInteractiveAsync(session.Page);
    }

    internal static bool IsBlazorHub(string url) =>
        new Uri(url).AbsolutePath.StartsWith(BrowserSession.BlazorHubPath, StringComparison.Ordinal);

    // The page renders "Prerendered" in Static SSR and "Interactive" once the circuit has rendered it.
    private static async Task WaitForInteractiveAsync(IPage page)
    {
        await Expect(page.Locator("#probe-render-state")).ToHaveTextAsync("Interactive");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Increment" })).ToBeEnabledAsync();
    }
}
