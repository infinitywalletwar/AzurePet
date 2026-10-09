using System.Reflection;
using System.Runtime.CompilerServices;

namespace InPolsure.ArchitectureTests;

/// <summary>What the namespace rule needs to know about a top-level type.</summary>
internal sealed record TypeShape(string FullName, string? Namespace, bool IsCompilerGenerated, bool IsEmbedded)
{
    // Roslyn and source generators mark the helper types they emit into our assemblies
    // with their own copy of this attribute, so it is matched by name.
    private const string EmbeddedAttributeName = "Microsoft.CodeAnalysis.EmbeddedAttribute";

    public static TypeShape From(Type type) => new(
        type.FullName ?? type.Name,
        type.Namespace,
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false),
        type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == EmbeddedAttributeName));
}

// Namespace rule (lab-01 §5 "Root namespace = assembly name"): every type in a production
// assembly lives in the assembly's root namespace or below it. Razor components get the
// project root namespace plus their folder, so they comply without special handling.
internal static class NamespaceRules
{
    // Razor emits generic type-inference helpers for generic components into
    // "__Blazor.<RootNamespace>.<Folder>.<Component>"; the rest of the name must still comply.
    private const string BlazorTypeInferencePrefix = "__Blazor.";

    public static IReadOnlyList<string> Violations(string assemblyName, IEnumerable<TypeShape> types) =>
        [.. types
            .Where(type => !IsExcluded(type) && !IsInRootNamespace(assemblyName, type.Namespace))
            .Select(type => $"{type.FullName} (namespace '{type.Namespace ?? "<global>"}')")
            .Order(StringComparer.Ordinal)];

    public static IEnumerable<TypeShape> TopLevelTypes(Assembly assembly) =>
        assembly.GetTypes().Where(type => !type.IsNested).Select(TypeShape.From);

    // Exclusions, each found by inspecting the real assemblies (keep minimal):
    // - Program: top-level statements put it in the global namespace (InPolsure.Web).
    // - [CompilerGenerated] or names starting with '<': e.g. <>z__ReadOnlyArray`1, emitted by
    //   the C# compiler for collection expressions (InPolsure.Web).
    // - [Microsoft.CodeAnalysis.Embedded]: Microsoft.CodeAnalysis.EmbeddedAttribute (Roslyn) and
    //   Microsoft.Extensions.Validation.Embedded.ValidatableTypeAttribute (the .NET 10 minimal-API
    //   validation source generator) in InPolsure.Web.
    private static bool IsExcluded(TypeShape type) =>
        (type.Namespace is null && type.FullName == "Program")
        || type.IsCompilerGenerated
        || type.FullName.StartsWith('<')
        || type.IsEmbedded;

    private static bool IsInRootNamespace(string assemblyName, string? @namespace)
    {
        if (@namespace is null)
        {
            return false;
        }

        if (@namespace.StartsWith(BlazorTypeInferencePrefix, StringComparison.Ordinal))
        {
            @namespace = @namespace[BlazorTypeInferencePrefix.Length..];
        }

        return @namespace == assemblyName
            || @namespace.StartsWith(assemblyName + ".", StringComparison.Ordinal);
    }
}
