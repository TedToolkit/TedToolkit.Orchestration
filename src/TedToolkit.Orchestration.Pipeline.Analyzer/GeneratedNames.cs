using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using TedToolkit.RoslynHelper;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Owns stable C# identifiers and Roslyn hint names derived from symbol identity.
internal static class GeneratedNames
{
    internal static string MetadataName(INamedTypeSymbol type)
    {
        var nameSpace = string.Join(".", NamespaceSegments(type.ContainingNamespace));
        return nameSpace.Length == 0 ? type.MetadataName : nameSpace + "." + type.MetadataName;
    }

    internal static string Namespace(INamespaceSymbol value)
    {
        var segments = new Stack<string>();
        for (var current = value; !current.IsGlobalNamespace; current = current.ContainingNamespace)
            segments.Push(current.Name.ToValidIdentifier());
        return string.Join(".", segments);
    }

    internal static string HintName(INamedTypeSymbol type, string suffix) =>
        MetadataName(type).ToHintNameKeepDot() + suffix;

    internal static string HintName(IMethodSymbol method, string suffix) =>
        (MetadataName(method.ContainingType) + "." + method.Name).ToHintNameKeepDot() + suffix;

    internal static string DisambiguateHintName(string hintName, string identity, string suffix)
    {
        var result = new StringBuilder(hintName.Length + 13);
        result.Append(hintName, 0, hintName.Length - suffix.Length)
            .Append('.').Append(StableSuffix(identity)).Append(suffix);
        return result.ToString();
    }

    internal static string ExtensionTypeName(INamedTypeSymbol type,
        bool includeNamespace = false, bool includeAssembly = false)
    {
        var segments = new List<string>();
        if (includeAssembly)
            segments.Add(type.ContainingAssembly.Identity.Name.ToHintName());
        if (includeNamespace)
        {
            if (type.ContainingNamespace.IsGlobalNamespace)
                segments.Add("Global");
            else
                segments.AddRange(NamespaceSegments(type.ContainingNamespace).Select(EscapeSegment));
        }
        segments.Add(EscapeSegment(type.Name) + "Extensions");
        return string.Join("_", segments).ToValidIdentifier();
    }

    internal static string ExtensionTypeName(IMethodSymbol method,
        bool includeNamespace = false, bool includeAssembly = false)
    {
        var type = method.ContainingType;
        var segments = new List<string>();
        if (includeAssembly)
            segments.Add(type.ContainingAssembly.Identity.Name.ToHintName());
        if (includeNamespace)
        {
            if (type.ContainingNamespace.IsGlobalNamespace)
                segments.Add("Global");
            else
                segments.AddRange(NamespaceSegments(type.ContainingNamespace).Select(EscapeSegment));
        }
        segments.Add(EscapeSegment(type.Name));
        segments.Add(EscapeSegment(method.Name) + "Extensions");
        return string.Join("_", segments).ToValidIdentifier();
    }

    internal static string DisambiguateIdentifier(string identifier, INamedTypeSymbol type) =>
        StableSuffix(type.ContainingAssembly.Identity + "|" + MetadataName(type)) + "_" + identifier;

    internal static string DisambiguateIdentifier(string identifier, IMethodSymbol method) =>
        StableSuffix(method.ContainingAssembly.Identity + "|" +
            MetadataName(method.ContainingType) + "|" + method.Name) + "_" + identifier;

    private static IEnumerable<string> NamespaceSegments(INamespaceSymbol value)
    {
        var segments = new Stack<string>();
        for (var current = value; !current.IsGlobalNamespace; current = current.ContainingNamespace)
            segments.Push(current.Name);
        return segments;
    }

    private static string EscapeSegment(string value) => value.Replace("_", "__");

    private static string StableSuffix(string identity)
    {
        byte[] hash;
        using (var algorithm = SHA256.Create())
            hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(identity));
        var result = new StringBuilder(12);
        for (var index = 0; index < 6; index++)
            result.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
        return result.ToString();
    }
}
