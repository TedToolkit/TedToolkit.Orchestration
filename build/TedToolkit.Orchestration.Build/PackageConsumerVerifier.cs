// -----------------------------------------------------------------------
// <copyright file="PackageConsumerVerifier.cs" company="TedToolkit">
// Copyright (c) TedToolkit. All rights reserved.
// Licensed under the LGPL-3.0 license. See COPYING, COPYING.LESSER file in the project root for full license information.
// </copyright>
// -----------------------------------------------------------------------

using System.IO.Compression;
using System.Xml.Linq;

using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;

using TedToolkit.ModularPipelines;

namespace TedToolkit.Orchestration.Build;

internal static class PackageConsumerVerifier
{
    internal static async Task VerifyAsync(
        IModuleContext context,
        IReadOnlyCollection<VerifiedPackage> packages,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(context.GetOutputFolder().Path, "package-consumer");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }

        var feed = Directory.CreateDirectory(Path.Combine(root, "feed"));
        var consumer = Directory.CreateDirectory(Path.Combine(root, "consumer"));
        var packagesRoot = Directory.CreateDirectory(Path.Combine(root, "packages"));

        foreach (var package in packages)
        {
            var destination = Path.Combine(feed.FullName, $"{package.Id}.{package.Version}.nupkg");
            ZipFile.CreateFromDirectory(package.Directory.FullName, destination);
        }

        var projectPath = Path.Combine(consumer.FullName, "PackageConsumer.csproj");
        await File.WriteAllTextAsync(
                projectPath,
                CreateProject(packages, packagesRoot.FullName),
                cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(consumer.FullName, "Program.cs"), ConsumerSource, cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(consumer.FullName, "NuGet.Config"),
                CreateNuGetConfig(feed.FullName, packagesRoot.FullName),
                cancellationToken)
            .ConfigureAwait(false);

        var result = await context.DotNet().Build(
                new()
                {
                    ProjectSolution = projectPath,
                    Configuration = "Release",
                    Arguments = ["--nologo",],
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode is not 0)
        {
            throw new InvalidOperationException("The package consumer failed to compile.");
        }

        DeliveryGateValidator.ValidateRestoredPackages(packages, feed.FullName, packagesRoot.FullName);
    }

    private static string CreateProject(IEnumerable<VerifiedPackage> packages, string packagesRoot)
    {
        var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup",
                new XElement("OutputType", "Exe"),
                new XElement("TargetFramework", "net10.0"),
                new XElement("LangVersion", "preview"),
                new XElement("ImplicitUsings", "enable"),
                new XElement("Nullable", "enable"),
                new XElement("ManagePackageVersionsCentrally", "false"),
                new XElement("RestorePackagesPath", packagesRoot)),
            new XElement("ItemGroup", packages.Select(package =>
                new XElement("PackageReference",
                    new XAttribute("Include", package.Id),
                    new XAttribute("Version", package.Version)))));

        return new XDocument(project).ToString();
    }

    private static string CreateNuGetConfig(string feed, string packagesRoot)
    {
        var configuration = new XElement("configuration",
            new XElement("config",
                new XElement("add",
                    new XAttribute("key", "globalPackagesFolder"),
                    new XAttribute("value", packagesRoot))),
            new XElement("packageSources",
                new XElement("clear"),
                new XElement("add", new XAttribute("key", "local"), new XAttribute("value", feed)),
                new XElement("add",
                    new XAttribute("key", "nuget.org"),
                    new XAttribute("value", "https://api.nuget.org/v3/index.json"))));

        return new XDocument(configuration).ToString();
    }

    private const string ConsumerSource = """
        using TedToolkit.Orchestration.StateMachine;

        Console.WriteLine(typeof(SmokePipeline).FullName);
        Console.WriteLine(typeof(SmokeMachine).FullName);

        public sealed partial class SmokePipeline : TedToolkit.Orchestration.Pipeline.Pipeline
        {
            protected override void Configuration(Builder pipeline)
            {
            }
        }

        public enum SmokeState
        {
            Initial,
            Complete,
        }

        [StateMachine<SmokeState>(SmokeState.Initial)]
        public sealed partial class SmokeMachine
        {
            [TransitionTo(SmokeState.Complete, SmokeState.Initial)]
            public partial ValueTask CompleteAsync();
        }
        """;
}
