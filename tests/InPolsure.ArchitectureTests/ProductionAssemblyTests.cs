using InPolsure.SharedKernel;

namespace InPolsure.ArchitectureTests;

// Applies the AC-14 rules to the real production assemblies.
public sealed class ProductionAssemblyTests
{
    [Fact]
    public void Load_ExpectedProductionAssemblies_MatchTypeAnchors()
    {
        Assert.Same(typeof(IClock).Assembly, ProductionAssemblies.Load(DependencyRules.SharedKernelName));
        Assert.Same(typeof(Program).Assembly, ProductionAssemblies.Load(DependencyRules.WebName));
        Assert.Equal(DependencyRules.UiName, ProductionAssemblies.Load(DependencyRules.UiName).GetName().Name);
    }

    [Fact]
    public void Load_MissingAssembly_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ProductionAssemblies.Load("InPolsure.DoesNotExist"));

        Assert.Contains("InPolsure.DoesNotExist", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadAll_OutputFolder_ContainsExpectedAndExcludesThisTestAssembly()
    {
        const string ThisAssembly = "InPolsure.ArchitectureTests";
        Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, ThisAssembly + ".dll")),
            $"{ThisAssembly}.dll is expected in {AppContext.BaseDirectory}, otherwise the test-assembly filter is not exercised.");

        var names = ProductionAssemblies.LoadAll().Select(assembly => assembly.GetName().Name).ToList();

        Assert.All(ProductionAssemblies.ExpectedNames, expected => Assert.Contains(expected, names));
        Assert.DoesNotContain(ThisAssembly, names);
    }

    [Fact]
    public void SharedKernel_References_NoOtherInPolsureOrAspNetCoreAssembly()
    {
        var assembly = ProductionAssemblies.Load(DependencyRules.SharedKernelName);

        var violations = DependencyRules.SharedKernelViolations(ProductionAssemblies.ReferencedNames(assembly));

        Assert.True(
            violations.Count == 0,
            DependencyRules.FailureMessage(
                DependencyRules.SharedKernelName,
                "no reference to other InPolsure.* or Microsoft.AspNetCore.* assemblies",
                violations));
    }

    [Fact]
    public void Ui_References_NoWebAssembly()
    {
        var assembly = ProductionAssemblies.Load(DependencyRules.UiName);

        var violations = DependencyRules.UiViolations(ProductionAssemblies.ReferencedNames(assembly));

        Assert.True(
            violations.Count == 0,
            DependencyRules.FailureMessage(DependencyRules.UiName, $"no reference to {DependencyRules.WebName}", violations));
    }

    [Theory]
    [InlineData(DependencyRules.SharedKernelName)]
    [InlineData(DependencyRules.UiName)]
    [InlineData(DependencyRules.WebName)]
    public void Types_InProductionAssembly_LiveInAssemblyRootNamespace(string assemblyName)
    {
        var assembly = ProductionAssemblies.Load(assemblyName);

        var violations = NamespaceRules.Violations(assemblyName, NamespaceRules.TopLevelTypes(assembly));

        Assert.True(
            violations.Count == 0,
            $"{assemblyName} violates rule 'every type lives in namespace {assemblyName} or below'. "
            + $"Offending types: {string.Join(", ", violations)}.");
    }

    [Fact]
    public void ProductionAssemblies_References_NoTestAssembly()
    {
        var failures = ProductionAssemblies.LoadAll()
            .Select(assembly => (
                Name: assembly.GetName().Name!,
                Violations: DependencyRules.TestReferenceViolations(ProductionAssemblies.ReferencedNames(assembly))))
            .Where(result => result.Violations.Count > 0)
            .Select(result => DependencyRules.FailureMessage(
                result.Name, "no reference to test projects, test frameworks or test-only packages", result.Violations))
            .ToList();

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
