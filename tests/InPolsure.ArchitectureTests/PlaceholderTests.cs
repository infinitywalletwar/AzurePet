using InPolsure.SharedKernel;

namespace InPolsure.ArchitectureTests;

// Wave 0 placeholder so that dotnet test runs; T-01.7 replaces it with the AC-14 rules.
public sealed class PlaceholderTests
{
    [Fact]
    public void Production_assemblies_load_with_expected_names()
    {
        Assert.Equal("InPolsure.SharedKernel", typeof(IClock).Assembly.GetName().Name);
        Assert.Equal("InPolsure.Web", typeof(Program).Assembly.GetName().Name);
    }
}
