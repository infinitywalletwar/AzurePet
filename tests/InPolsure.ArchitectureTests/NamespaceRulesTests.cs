namespace InPolsure.ArchitectureTests;

// Proves that the namespace rule reports violations and honours only its documented exclusions.
public sealed class NamespaceRulesTests
{
    private const string Assembly = "InPolsure.Web";

    [Theory]
    [InlineData("InPolsure.Web")]
    [InlineData("InPolsure.Web.Components")]
    [InlineData("InPolsure.Web.Components.Pages")]
    [InlineData("__Blazor.InPolsure.Web.Components.Pages.Probe")]
    public void Violations_TypeInRootNamespaceOrBelow_ReportsNothing(string @namespace)
    {
        var violations = NamespaceRules.Violations(Assembly, [Shape(@namespace + ".Probe", @namespace)]);

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("InPolsure.Ui")]
    [InlineData("InPolsure.WebHost")]
    [InlineData("Microsoft.AspNetCore.Components")]
    [InlineData("__Blazor.InPolsure.Ui.Components")]
    [InlineData("Web")]
    public void Violations_TypeOutsideRootNamespace_IsReported(string @namespace)
    {
        var violations = NamespaceRules.Violations(Assembly, [Shape(@namespace + ".Stray", @namespace)]);

        Assert.Equal([$"{@namespace}.Stray (namespace '{@namespace}')"], violations);
    }

    [Fact]
    public void Violations_NonProgramTypeInGlobalNamespace_IsReported()
    {
        var violations = NamespaceRules.Violations(Assembly, [Shape("Startup", null)]);

        Assert.Equal(["Startup (namespace '<global>')"], violations);
    }

    [Fact]
    public void Violations_DocumentedExclusions_ReportsNothing()
    {
        TypeShape[] types =
        [
            Shape("Program", null),
            new("<>z__ReadOnlyArray`1", null, IsCompilerGenerated: true, IsEmbedded: false),
            new("<PrivateImplementationDetails>", null, IsCompilerGenerated: false, IsEmbedded: false),
            new("Microsoft.CodeAnalysis.EmbeddedAttribute", "Microsoft.CodeAnalysis", IsCompilerGenerated: false, IsEmbedded: true),
        ];

        Assert.Empty(NamespaceRules.Violations(Assembly, types));
    }

    [Fact]
    public void Violations_ProgramInNamedNamespaceOutsideRoot_IsReported()
    {
        var violations = NamespaceRules.Violations(Assembly, [Shape("Other.Program", "Other")]);

        Assert.Equal(["Other.Program (namespace 'Other')"], violations);
    }

    private static TypeShape Shape(string fullName, string? @namespace) =>
        new(fullName, @namespace, IsCompilerGenerated: false, IsEmbedded: false);
}
