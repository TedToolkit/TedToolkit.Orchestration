// -----------------------------------------------------------------------
// <copyright file="DeliveryGateValidator.cs" company="TedToolkit">
// Copyright (c) TedToolkit. All rights reserved.
// Licensed under the LGPL-3.0 license. See COPYING, COPYING.LESSER file in the project root for full license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Xml.Linq;
using System.Security.Cryptography;

namespace TedToolkit.Orchestration.Build;

internal static class DeliveryGateValidator
{
    private static readonly PackageContract[] PackageContracts =
    [
        new(
            "TedToolkit.Orchestration.Pipeline",
            "lib/net10.0/TedToolkit.Orchestration.Pipeline.dll",
            "analyzers/dotnet/cs/TedToolkit.Orchestration.Pipeline.Analyzer.dll"),
        new(
            "TedToolkit.Orchestration.StateMachine",
            "lib/net10.0/TedToolkit.Orchestration.StateMachine.dll",
            "analyzers/dotnet/cs/TedToolkit.Orchestration.StateMachine.Analyzer.dll"),
    ];

    internal static IReadOnlyList<VerifiedPackage> Validate(
        string failedProjects,
        IReadOnlyCollection<string> expectedTestProjects,
        IReadOnlyCollection<TestReportSummary> reports,
        string packageRoot)
    {
        if (!string.IsNullOrWhiteSpace(failedProjects))
        {
            throw new InvalidOperationException($"Build failed for: {failedProjects.Trim()}");
        }

        foreach (var project in expectedTestProjects)
        {
            var matchingReports = reports
                .Where(report => report.FileName.StartsWith(project + "_", StringComparison.Ordinal))
                .ToArray();

            if (matchingReports.Length is not 1)
            {
                throw new InvalidOperationException(
                    $"Expected one test report for {project}, but found {matchingReports.Length}.");
            }

            var report = matchingReports[0];
            if (report.Executed <= 0 || report.Executed != report.Passed)
            {
                throw new InvalidOperationException(
                    $"Test report {report.FileName} executed {report.Executed} tests and passed {report.Passed}.");
            }
        }

        if (!Directory.Exists(packageRoot))
        {
            throw new InvalidOperationException($"Package output does not exist: {packageRoot}");
        }

        return PackageContracts.Select(contract => ValidatePackage(packageRoot, contract)).ToArray();
    }

    internal static void ValidateRestoredPackages(
        IReadOnlyCollection<VerifiedPackage> packages,
        string feedRoot,
        string packagesRoot)
    {
        foreach (var package in packages)
        {
            var fileName = $"{package.Id}.{package.Version}.nupkg";
            var candidatePath = Path.Combine(feedRoot, fileName);
            var restoredPath = Path.Combine(
                packagesRoot,
                package.Id.ToLowerInvariant(),
                package.Version.ToLowerInvariant(),
                fileName.ToLowerInvariant());

            if (!File.Exists(candidatePath) || !File.Exists(restoredPath))
            {
                throw new InvalidOperationException($"The isolated restore did not contain {fileName}.");
            }

            var candidateHash = SHA256.HashData(File.ReadAllBytes(candidatePath));
            var restoredHash = SHA256.HashData(File.ReadAllBytes(restoredPath));
            if (!candidateHash.SequenceEqual(restoredHash))
            {
                throw new InvalidOperationException($"The restored {fileName} does not match the candidate package.");
            }
        }
    }

    private static VerifiedPackage ValidatePackage(string packageRoot, PackageContract contract)
    {
        var matches = Directory.EnumerateDirectories(packageRoot)
            .Select(ReadPackage)
            .Where(package => string.Equals(package.Id, contract.Id, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length is not 1)
        {
            throw new InvalidOperationException(
                $"Expected one extracted package for {contract.Id}, but found {matches.Length}.");
        }

        var package = matches[0];
        foreach (var relativePath in new[] { contract.RuntimePath, contract.AnalyzerPath, })
        {
            var path = Path.Combine(package.Directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"Package {contract.Id} is missing {relativePath}.");
            }
        }

        return package;
    }

    private static VerifiedPackage ReadPackage(string path)
    {
        var directory = new DirectoryInfo(path);
        var nuspecFiles = directory.GetFiles("*.nuspec", SearchOption.TopDirectoryOnly);
        if (nuspecFiles.Length is not 1)
        {
            return new("", "", directory);
        }

        var document = XDocument.Load(nuspecFiles[0].FullName);
        var metadata = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName is "metadata");
        var id = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName is "id")?.Value;
        var version = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName is "version")?.Value;
        return new(id ?? "", version ?? "", directory);
    }

    private sealed record PackageContract(string Id, string RuntimePath, string AnalyzerPath);
}

internal sealed record TestReportSummary(string FileName, int Executed, int Passed);

internal sealed record VerifiedPackage(string Id, string Version, DirectoryInfo Directory);
