using Bunit;
using InPolsure.Ui.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace InPolsure.UnitTests.Ui;

public sealed class AppShellTests : BunitContext
{
    [Fact]
    public void Render_default_has_one_header_main_and_footer_landmark()
    {
        var cut = Render<AppShell>(parameters => parameters.AddChildContent("<p>Page</p>"));

        Assert.Single(cut.FindAll("header"));
        Assert.Single(cut.FindAll("main"));
        Assert.Single(cut.FindAll("footer"));
        Assert.Equal("Page", cut.Find("main p").TextContent);
    }

    [Fact]
    public void Render_default_shows_product_name_as_home_link_in_header()
    {
        var cut = Render<AppShell>();

        var brand = cut.Find("header a");
        Assert.Equal("InPolsure", brand.TextContent);
        Assert.Equal("/", brand.GetAttribute("href"));
    }

    [Fact]
    public void Render_with_product_name_uses_it_in_header_and_default_footer()
    {
        var cut = Render<AppShell>(parameters => parameters.Add(p => p.ProductName, "Acme Claims"));

        Assert.Equal("Acme Claims", cut.Find("header a").TextContent);
        Assert.Equal("Acme Claims", cut.Find("footer").TextContent.Trim());
    }

    [Fact]
    public void Render_with_footer_content_replaces_default_footer()
    {
        var cut = Render<AppShell>(parameters => parameters.Add(p => p.FooterContent, "<p class=\"custom\">Help</p>"));

        Assert.Equal("Help", cut.Find("footer p.custom").TextContent);
        Assert.Empty(cut.FindAll("footer .app-footer__text"));
    }

    [Fact]
    public void Render_default_skip_link_is_first_element_and_targets_main()
    {
        var cut = Render<AppShell>();

        var first = cut.Nodes.OfType<AngleSharp.Dom.IElement>().First();
        Assert.Equal("A", first.TagName);
        Assert.Contains("skip-link", first.ClassList);
        Assert.Equal("Skip to main content", first.TextContent);
        Assert.Equal($"/#{AppShell.MainContentId}", first.GetAttribute("href"));

        var main = cut.Find("main");
        Assert.Equal(AppShell.MainContentId, main.Id);
        Assert.Equal("-1", main.GetAttribute("tabindex"));
    }

    [Fact]
    public void Render_default_skip_link_opts_out_of_enhanced_navigation()
    {
        var cut = Render<AppShell>();

        Assert.Equal("false", cut.Find("a.skip-link").GetAttribute("data-enhance-nav"));
    }

    [Fact]
    public void Navigation_to_other_page_skip_link_targets_main_on_that_page()
    {
        var cut = Render<AppShell>();

        Services.GetRequiredService<NavigationManager>().NavigateTo("/claims/42?tab=notes");

        cut.WaitForAssertion(() =>
            Assert.Equal("/claims/42?tab=notes#main-content", cut.Find("a.skip-link").GetAttribute("href")));
    }
}
