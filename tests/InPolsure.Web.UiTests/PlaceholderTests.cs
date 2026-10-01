namespace InPolsure.Web.UiTests;

// Wave 0 placeholder so that dotnet test runs without browsers; T-01.9 replaces it
// with the Playwright tests.
public sealed class PlaceholderTests
{
    [Fact]
    public void Web_assembly_loads_with_expected_name() =>
        Assert.Equal("InPolsure.Web", typeof(Program).Assembly.GetName().Name);
}
