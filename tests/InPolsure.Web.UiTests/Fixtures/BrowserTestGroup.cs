namespace InPolsure.Web.UiTests.Fixtures;

/// <summary>xUnit collection: all browser tests share one app and one browser (<see cref="UiTestFixture"/>).</summary>
[CollectionDefinition(Name)]
public sealed class BrowserTestGroup : ICollectionFixture<UiTestFixture>
{
    public const string Name = "Browser";
}
