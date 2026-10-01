using Bunit;
using InPolsure.Ui.Components;

namespace InPolsure.UnitTests.Ui;

public sealed class NoticeTests : BunitContext
{
    [Theory]
    [InlineData(NoticeSeverity.Info, "status")]
    [InlineData(NoticeSeverity.Warning, "status")]
    [InlineData(NoticeSeverity.Error, "alert")]
    public void Render_with_severity_sets_aria_role(NoticeSeverity severity, string expectedRole)
    {
        var cut = Render<Notice>(parameters => parameters
            .Add(p => p.Severity, severity)
            .AddChildContent("Message"));

        Assert.Equal(expectedRole, cut.Find(".notice").GetAttribute("role"));
    }

    [Theory]
    [InlineData(NoticeSeverity.Info, "Information:", "notice--info")]
    [InlineData(NoticeSeverity.Warning, "Warning:", "notice--warning")]
    [InlineData(NoticeSeverity.Error, "Error:", "notice--error")]
    public void Render_with_severity_names_it_in_visually_hidden_text_not_only_colour(
        NoticeSeverity severity, string expectedLabel, string expectedClass)
    {
        var cut = Render<Notice>(parameters => parameters
            .Add(p => p.Severity, severity)
            .AddChildContent("Message"));

        Assert.Equal(expectedLabel, cut.Find(".visually-hidden").TextContent.Trim());
        Assert.Contains(expectedClass, cut.Find(".notice").ClassList);
    }

    [Fact]
    public void Render_default_is_info()
    {
        var cut = Render<Notice>(parameters => parameters.AddChildContent("Message"));

        Assert.Equal("status", cut.Find(".notice").GetAttribute("role"));
        Assert.Contains("notice--info", cut.Find(".notice").ClassList);
    }

    [Fact]
    public void Render_with_title_shows_title_and_message()
    {
        var cut = Render<Notice>(parameters => parameters
            .Add(p => p.Title, "Saved")
            .AddChildContent("Your changes were saved."));

        Assert.Equal("Saved", cut.Find("strong.notice__title").TextContent);
        Assert.Contains("Your changes were saved.", cut.Find(".notice").TextContent);
    }

    [Fact]
    public void Render_without_title_has_no_title_element()
    {
        var cut = Render<Notice>(parameters => parameters.AddChildContent("Message"));

        Assert.Empty(cut.FindAll(".notice__title"));
    }
}
