using System.Reflection;

namespace InPolsure.ArchitectureTests;

// Loads the production assemblies the rules run against. A missing assembly throws,
// so a rule can never pass silently because its subject was not found.
internal static class ProductionAssemblies
{
    public static readonly IReadOnlyList<string> ExpectedNames =
    [
        DependencyRules.SharedKernelName,
        DependencyRules.UiName,
        DependencyRules.WebName,
    ];

    public static Assembly Load(string assemblyName)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (FileNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"Production assembly '{assemblyName}' could not be loaded. "
                + "Check the ProjectReference in InPolsure.ArchitectureTests.csproj.",
                ex);
        }

        if (!string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Loading '{assemblyName}' returned '{assembly.GetName().Name}'.");
        }

        return assembly;
    }

    /// <summary>
    /// The expected production assemblies plus every other non-test InPolsure.*.dll in the
    /// test output folder, so assemblies added later are covered by the generic rules.
    /// </summary>
    public static IReadOnlyList<Assembly> LoadAll()
    {
        var discovered = Directory
            .EnumerateFiles(AppContext.BaseDirectory, DependencyRules.AssemblyPrefix + "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => !DependencyRules.IsTestAssembly(name));

        return [.. ExpectedNames
            .Concat(discovered)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .Select(Load)];
    }

    public static IEnumerable<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name).OfType<string>();
}
