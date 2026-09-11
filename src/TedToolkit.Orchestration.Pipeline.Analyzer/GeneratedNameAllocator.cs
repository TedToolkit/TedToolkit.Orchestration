using System;
using System.Collections.Generic;

namespace TedToolkit.Orchestration.Pipeline.Analyzer;

// Allocates generated identifiers against every user and generated name in one C# scope.
internal sealed class GeneratedNameAllocator
{
    private readonly HashSet<string> _used;

    internal GeneratedNameAllocator(IEnumerable<string> used) =>
        _used = new HashSet<string>(used, StringComparer.Ordinal);

    internal string Allocate(string proposed)
    {
        while (!_used.Add(proposed)) proposed += "_";
        return proposed;
    }
}
