using Bunit;
using InPolsure.Ui.Components;

namespace InPolsure.UnitTests.Ui;

public sealed class ButtonTests : BunitContext
{
    [Fact]
    public void Render_default_is_native_primary_button_with_type_button()
    {
        var cut = Render<Button>(parameters => parameters.AddChildContent("Save"));

        var button = cut.Find("button");
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("Save", button.TextContent.Trim());
        Assert.Contains("button--primary", button.ClassList);
        Assert.False(button.HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(ButtonType.Button, "button")]
    [InlineData(ButtonType.Submit, "submit")]
    [InlineData(ButtonType.Reset, "reset")]
    public void Render_with_type_sets_html_type(ButtonType type, string expected)
    {
        var cut = Render<Button>(parameters => parameters
            .Add(p => p.Type, type)
            .AddChildContent("Go"));

        Assert.Equal(expected, cut.Find("button").GetAttribute("type"));
    }

    [Fact]
    public void Render_secondary_variant_sets_secondary_class()
    {
        var cut = Render<Button>(parameters => parameters
            .Add(p => p.Variant, ButtonVariant.Secondary)
            .AddChildContent("Cancel"));

        var button = cut.Find("button");
        Assert.Contains("button--secondary", button.ClassList);
        Assert.DoesNotContain("button--primary", button.ClassList);
    }

    [Fact]
    public void Render_disabled_sets_disabled_attribute()
    {
        var cut = Render<Button>(parameters => parameters
            .Add(p => p.Disabled, true)
            .AddChildContent("Save"));

        Assert.True(cut.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void Click_invokes_on_click_callback()
    {
        var clicks = 0;
        var cut = Render<Button>(parameters => parameters
            .Add(p => p.OnClick, () => clicks++)
            .AddChildContent("Increment"));

        cut.Find("button").Click();

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Render_with_additional_attributes_passes_them_to_button()
    {
        var cut = Render<Button>(parameters => parameters
            .AddUnmatched("aria-label", "Close dialog")
            .AddUnmatched("name", "action")
            .AddChildContent("X"));

        var button = cut.Find("button");
        Assert.Equal("Close dialog", button.GetAttribute("aria-label"));
        Assert.Equal("action", button.GetAttribute("name"));
    }
}
