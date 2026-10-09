using System.Globalization;
using System.Net;
using InPolsure.Web.UiTests.Fixtures;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace InPolsure.Web.UiTests;

/// <summary>Home page in a real browser: AC-04 (Static SSR), AC-07 (CSP, axe), AC-09 (360 px), focus indicator.</summary>
[Collection(BrowserTestGroup.Name)]
public sealed class HomePageTests(UiTestFixture fixture)
{
    [Fact]
    public async Task Get_root_renders_english_static_ssr_page_without_blazor_circuit()
    {
        await using var session = await fixture.NewSessionAsync();

        var response = await session.GotoAsync("/");
        await session.WaitForBlazorScriptAsync();

        Assert.Equal((int)HttpStatusCode.OK, response.Status);
        Assert.Equal("en", await session.Page.Locator("html").GetAttributeAsync("lang"));
        Assert.Contains("InPolsure", await session.Page.TitleAsync(), StringComparison.Ordinal);

        // blazor.web.js is loaded on every page but starts no circuit when no component is interactive (ADR-0008).
        Assert.Empty(session.WebSocketUrls);
        Assert.Empty(session.BlazorHubRequests);
    }

    [Fact]
    public async Task Get_root_has_no_csp_or_axe_violations()
    {
        await using var session = await fixture.NewSessionAsync();

        await session.GotoAsync("/");
        await session.WaitForBlazorScriptAsync();

        await session.AssertNoCspViolationsAsync();
        await session.AssertNoAxeViolationsAsync();
    }

    [Fact]
    public async Task Get_root_at_360px_viewport_has_no_horizontal_scroll()
    {
        await using var session = await fixture.NewSessionAsync(new ViewportSize { Width = 360, Height = 740 });

        await session.GotoAsync("/");

        var widths = await session.Page.EvaluateAsync<int[]>(
            "() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]");
        var (scrollWidth, clientWidth) = (widths[0], widths[1]);
        Assert.Equal(360, clientWidth);
        Assert.True(scrollWidth <= clientWidth, $"Horizontal scroll: scrollWidth {scrollWidth} > clientWidth {clientWidth}.");
    }

    [Fact]
    public async Task Tab_on_root_shows_skip_link_then_brand_link_with_focus_ring_contrasting_with_header()
    {
        await using var session = await fixture.NewSessionAsync();
        await session.GotoAsync("/");
        var page = session.Page;
        var headerBackground = await ComputedStyleAsync(page.Locator(".app-header"), "background-color");

        // First Tab: the skip link is the first focusable element, moves into view and shows a focus ring.
        await page.Keyboard.PressAsync("Tab");
        var skipLink = page.Locator("a.skip-link");
        await Expect(skipLink).ToBeFocusedAsync();
        await Expect(skipLink).ToBeInViewportAsync(new() { Ratio = 1 });
        await AssertFocusRingContrastsWithAsync(skipLink, headerBackground);

        // The skip link's own colours keep its text readable (it overlaps the header).
        var skipText = Colour.Parse(await ComputedStyleAsync(skipLink, "color"));
        var skipBackground = Colour.Parse(await ComputedStyleAsync(skipLink, "background-color"));
        Assert.True(Colour.Contrast(skipText, skipBackground) >= 4.5, "Skip link text contrast is below 4.5:1.");

        await session.AssertNoAxeViolationsAsync();

        // Second Tab: the product name in the header, whose background is the brand colour.
        await page.Keyboard.PressAsync("Tab");
        var brand = page.Locator(".app-header__brand");
        await Expect(brand).ToBeFocusedAsync();
        await AssertFocusRingContrastsWithAsync(brand, headerBackground);

        await session.AssertNoCspViolationsAsync();
    }

    // WCAG 2.1 SC 1.4.11 (non-text contrast) and 2.4.7: the focus ring must be visible against the header colour.
    private static async Task AssertFocusRingContrastsWithAsync(ILocator element, string adjacentBackground)
    {
        Assert.Equal("solid", await ComputedStyleAsync(element, "outline-style"));
        var width = await ComputedStyleAsync(element, "outline-width");
        Assert.True(Pixels(width) >= 2, $"Focus ring width {width} is below 2px.");

        var outline = await ComputedStyleAsync(element, "outline-color");
        var contrast = Colour.Contrast(Colour.Parse(outline), Colour.Parse(adjacentBackground));
        Assert.True(contrast >= 3, $"Focus ring {outline} on {adjacentBackground} has contrast {contrast:F2}:1, below 3:1.");
    }

    private static Task<string> ComputedStyleAsync(ILocator element, string property) =>
        element.EvaluateAsync<string>("(el, p) => getComputedStyle(el).getPropertyValue(p)", property);

    private static double Pixels(string cssLength) =>
        double.Parse(cssLength.Replace("px", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
}
