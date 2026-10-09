using InPolsure.Web.UiTests.Fixtures;
using static Microsoft.Playwright.Assertions;

namespace InPolsure.Web.UiTests;

/// <summary>
/// AC-08 (ADR-0008, ADR-0017 item 5): when the connection to the server is lost, the template's
/// <c>ReconnectModal</c> dialog appears, under the strict CSP.
/// </summary>
/// <remarks>
/// The outage is a hard server kill on an app instance of its own (the shared app keeps serving the other tests).
/// Playwright's offline mode (<c>SetOfflineAsync(true)</c>) is not used: in Chromium it blocks new requests but
/// leaves an already open WebSocket connected, so the circuit never notices the cut.
/// </remarks>
[Collection(BrowserTestGroup.Name)]
public sealed class ReconnectModalTests(UiTestFixture fixture)
{
    [Fact]
    public async Task Server_killed_on_probe_page_shows_reconnect_modal_without_csp_violations()
    {
        await using var app = fixture.CreateDedicatedApp();
        var baseAddress = app.StartAndGetBaseAddress();
        await using var session = await fixture.NewSessionAsync(baseAddress: baseAddress);
        await InteractiveProbeTests.OpenInteractiveProbeAsync(session);
        var dialog = session.Page.Locator("#components-reconnect-modal");
        await Expect(dialog).ToBeHiddenAsync();

        await app.KillServerAsync();

        // Modal dialog (showModal), opened by ReconnectModal.razor.js when blazor.web.js reports the lost circuit.
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog).ToHaveAttributeAsync("open", string.Empty);
        await Expect(dialog).ToHaveAccessibleNameAsync("Connection to the server");
        await session.AssertNoCspViolationsAsync();
    }
}
