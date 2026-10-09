namespace InPolsure.ArchitectureTests;

// Project dependency and namespace rules of docs/labs/lab-01-walking-skeleton.md AC-14
// and T-01.7.
//
// Each rule is a pure function over referenced-assembly names (or type descriptions), so
// it can be proven to detect violations with synthetic input (DependencyRulesTests,
// NamespaceRulesTests) and then applied to the real assemblies (ProductionAssemblyTests).
//
// Limits of reference-based rules:
// - Assembly.GetReferencedAssemblies() lists only assemblies whose metadata the compiled
//   code uses. A ProjectReference, PackageReference or FrameworkReference without any type
//   usage leaves no trace in the assembly, so it is not detected here. A csproj-level check
//   (parsing project files or project.assets.json) can be added in Lab 03 if needed.
// - Constants (const fields) are inlined by the compiler, so using only a constant from a
//   forbidden assembly emits no reference either.
//
// Module rules (from Lab 03, ADR-0001):
// - Modules live in src/Modules/<Module>/InPolsure.<Module>/ (one project per module).
//   Public contract types are in the InPolsure.<Module>.Contracts namespace; everything
//   else is internal.
// - Lab 03 adds the module assemblies to ProductionAssemblies.ExpectedNames (the test
//   project then needs a ProjectReference per module) and adds assembly-level rules here:
//   a module may reference InPolsure.SharedKernel and other module assemblies (for their
//   contracts only, see below); InPolsure.SharedKernel and InPolsure.Ui must not reference
//   any module. Only InPolsure.Web (the host) may reference every module; it composes them.
// - "A module uses only other modules' InPolsure.<Other>.Contracts types" is a type-level
//   rule: it is checked with Type.Namespace over the types a module uses. The library or
//   analyzer for type-level rules (e.g. the IgnoreQueryFilters() ban) is chosen in Lab 04;
//   until then such rules are written with reflection in this project.
internal static class DependencyRules
{
    public const string AssemblyPrefix = "InPolsure.";
    public const string SharedKernelName = "InPolsure.SharedKernel";
    public const string UiName = "InPolsure.Ui";
    public const string WebName = "InPolsure.Web";

    // Suffixes of our own test projects (tests/InPolsure.*.Tests etc.).
    private static readonly string[] _testAssemblySuffixes =
    [
        ".Tests",
        ".UnitTests",
        ".IntegrationTests",
        ".ArchitectureTests",
        ".UiTests",
    ];

    // Suffix of test-helper packages, e.g. Microsoft.AspNetCore.Mvc.Testing,
    // Microsoft.Extensions.TimeProvider.Testing, Microsoft.Extensions.Diagnostics.Testing.
    private const string TestingPackageSuffix = ".Testing";

    // Name prefixes of test frameworks and test-only packages (lab-01 §5 package list plus
    // common mocking/assertion/container libraries that must never ship in production).
    private static readonly string[] _testFrameworkPrefixes =
    [
        "xunit",
        "bunit",
        "Microsoft.NET.Test.Sdk",
        "Microsoft.TestPlatform",
        "Microsoft.VisualStudio.TestPlatform",
        "Microsoft.Testing.",
        "Microsoft.CodeCoverage",
        "Microsoft.Playwright",
        "Deque.AxeCore",
        "Microsoft.AspNetCore.TestHost",
        "OpenTelemetry.Exporter.InMemory",
        "Testcontainers",
        "NSubstitute",
        "Moq",
        "FluentAssertions",
        "AwesomeAssertions",
        "Shouldly",
        "AutoFixture",
    ];

    /// <summary>InPolsure.SharedKernel references no other InPolsure assembly and no ASP.NET Core assembly.</summary>
    public static IReadOnlyList<string> SharedKernelViolations(IEnumerable<string> referencedAssemblyNames) =>
        Offending(referencedAssemblyNames, name => IsInPolsureAssembly(name) || IsAspNetCoreAssembly(name));

    /// <summary>InPolsure.Ui does not reference InPolsure.Web.</summary>
    public static IReadOnlyList<string> UiViolations(IEnumerable<string> referencedAssemblyNames) =>
        Offending(referencedAssemblyNames, name => string.Equals(name, WebName, StringComparison.OrdinalIgnoreCase));

    /// <summary>A non-test assembly references no test project, test framework or test-only package.</summary>
    public static IReadOnlyList<string> TestReferenceViolations(IEnumerable<string> referencedAssemblyNames) =>
        Offending(referencedAssemblyNames, IsTestAssembly);

    public static bool IsInPolsureAssembly(string assemblyName) =>
        assemblyName.StartsWith(AssemblyPrefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsAspNetCoreAssembly(string assemblyName) =>
        assemblyName.StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase);

    public static bool IsTestAssembly(string assemblyName) =>
        _testAssemblySuffixes.Any(suffix => assemblyName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        || assemblyName.EndsWith(TestingPackageSuffix, StringComparison.OrdinalIgnoreCase)
        || _testFrameworkPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static string FailureMessage(string assemblyName, string rule, IEnumerable<string> offendingReferences) =>
        $"{assemblyName} violates rule '{rule}'. Offending references: {string.Join(", ", offendingReferences)}.";

    private static string[] Offending(IEnumerable<string> referencedAssemblyNames, Func<string, bool> isForbidden) =>
        [.. referencedAssemblyNames
            .Where(isForbidden)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)];
}
