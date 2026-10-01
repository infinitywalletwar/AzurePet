using AngleSharp.Dom;
using Bunit;
using InPolsure.Ui.Components;
using InPolsure.Ui.Layout;

namespace InPolsure.UnitTests.Ui;

/// <summary>
/// ADR-0017 item 4 and lab-01 AC-17: components must work under <c>style-src 'self'; script-src 'self'</c>,
/// so their markup must contain no <c>style</c> attributes and no <c>style</c> or <c>script</c> elements.
/// </summary>
public sealed class NoInlineStylesTests : BunitContext
{
    private const string InlineStyleOrScript = "[style], style, script";

    private static readonly Dictionary<string, Func<BunitContext, IReadOnlyList<IElement>>> _cases = new()
    {
        ["AppShell"] = ctx => ctx.Render<AppShell>(p => p
            .Add(x => x.HeaderContent, "<nav>Menu</nav>")
            .AddChildContent("<p>Body</p>")).FindAll(InlineStyleOrScript),
        ["Button"] = ctx => ctx.Render<Button>(p => p
            .AddChildContent("Save")).FindAll(InlineStyleOrScript),
        ["Button secondary disabled submit"] = ctx => ctx.Render<Button>(p => p
            .Add(x => x.Variant, ButtonVariant.Secondary)
            .Add(x => x.Disabled, true)
            .Add(x => x.Type, ButtonType.Submit)
            .AddChildContent("Send")).FindAll(InlineStyleOrScript),
        ["Notice info with title"] = ctx => ctx.Render<Notice>(p => p
            .Add(x => x.Severity, NoticeSeverity.Info)
            .Add(x => x.Title, "Note")
            .AddChildContent("Info")).FindAll(InlineStyleOrScript),
        ["Notice warning"] = ctx => ctx.Render<Notice>(p => p
            .Add(x => x.Severity, NoticeSeverity.Warning)
            .AddChildContent("Careful")).FindAll(InlineStyleOrScript),
        ["Notice error"] = ctx => ctx.Render<Notice>(p => p
            .Add(x => x.Severity, NoticeSeverity.Error)
            .AddChildContent("Failed")).FindAll(InlineStyleOrScript),
    };

    public static TheoryData<string> CaseNames => [.. _cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Render_component_has_no_inline_style_or_script(string caseName)
    {
        var offending = _cases[caseName](this).Select(element => element.OuterHtml);

        Assert.Empty(offending);
    }
}
