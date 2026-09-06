using System.IO.Compression;
using TedToolkit.Orchestration.Build;

namespace TedToolkit.Orchestration.Build.Tests;

public class DeliveryGateValidatorTests
{
    [Test]
    public async Task AcceptsCompleteReportsAndPackageLayouts()
    {
        using var fixture = GateFixture.Create();

        var packages = DeliveryGateValidator.Validate("", fixture.ExpectedTests, fixture.PassingReports, fixture.Root);

        await Assert.That(packages.Count).IsEqualTo(2);
        await Assert.That(packages.Any(package => package.Id is "TedToolkit.Orchestration.Pipeline")).IsTrue();
        await Assert.That(packages.Any(package => package.Id is "TedToolkit.Orchestration.StateMachine")).IsTrue();
    }

    [Test]
    public async Task RejectsBuildFailures()
    {
        using var fixture = GateFixture.Create();
        var failed = Throws(() => DeliveryGateValidator.Validate(
            "TedToolkit.Orchestration.slnx",
            fixture.ExpectedTests,
            fixture.PassingReports,
            fixture.Root));

        await Assert.That(failed).IsTrue();
    }

    [Test]
    public async Task RejectsMissingOrFailedTestReports()
    {
        using var fixture = GateFixture.Create();
        var missing = Throws(() => DeliveryGateValidator.Validate(
            "",
            fixture.ExpectedTests,
            fixture.PassingReports.Take(1).ToArray(),
            fixture.Root));
        var failed = Throws(() => DeliveryGateValidator.Validate(
            "",
            fixture.ExpectedTests,
            [fixture.PassingReports[0], new(fixture.PassingReports[1].FileName, 2, 1),],
            fixture.Root));

        await Assert.That(missing).IsTrue();
        await Assert.That(failed).IsTrue();
    }

    [Test]
    public async Task RejectsIncompletePackage()
    {
        using var fixture = GateFixture.Create();
        File.Delete(Path.Combine(
            fixture.Root,
            "TedToolkit.Orchestration.StateMachine.1.0.0",
            "analyzers",
            "dotnet",
            "cs",
            "TedToolkit.Orchestration.StateMachine.Analyzer.dll"));

        var failed = Throws(() => DeliveryGateValidator.Validate(
            "",
            fixture.ExpectedTests,
            fixture.PassingReports,
            fixture.Root));

        await Assert.That(failed).IsTrue();
    }

    [Test]
    public async Task RequiresCandidatePackagesInTheIsolatedRestoreCache()
    {
        using var fixture = GateFixture.Create();
        var packages = DeliveryGateValidator.Validate(
            "",
            fixture.ExpectedTests,
            fixture.PassingReports,
            fixture.Root);
        var feed = Directory.CreateDirectory(Path.Combine(fixture.Root, "feed"));
        var isolatedCache = Directory.CreateDirectory(Path.Combine(fixture.Root, "isolated-cache"));
        var staleCache = Directory.CreateDirectory(Path.Combine(fixture.Root, "stale-cache"));

        foreach (var package in packages)
        {
            var fileName = $"{package.Id}.{package.Version}.nupkg";
            var candidatePath = Path.Combine(feed.FullName, fileName);
            ZipFile.CreateFromDirectory(package.Directory.FullName, candidatePath);
            WriteCachedPackage(isolatedCache.FullName, package, File.ReadAllBytes(candidatePath));
            WriteCachedPackage(staleCache.FullName, package, "stale"u8.ToArray());
        }

        DeliveryGateValidator.ValidateRestoredPackages(packages, feed.FullName, isolatedCache.FullName);
        var staleWasRejected = Throws(() => DeliveryGateValidator.ValidateRestoredPackages(
            packages,
            feed.FullName,
            staleCache.FullName));

        await Assert.That(staleWasRejected).IsTrue();
    }

    [Test]
    public async Task GateParticipatesInTedToolkitReleaseBarrier()
    {
        var repositoryRoot = FindRepositoryRoot();
        var gateSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "build",
            "TedToolkit.Orchestration.Build",
            "RepositoryDeliveryGateModule.cs"));
        var programSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "build",
            "TedToolkit.Orchestration.Build",
            "Program.cs"));
        var releaseSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "externals",
            "TedToolkit",
            "TedToolkit.ModularPipelines",
            "Modules",
            "04_Release",
            "ReleaseModule.cs"));

        await Assert.That(gateSource).Contains("[DependsOn<TestModule>]");
        await Assert.That(gateSource).Contains(": CompileCheckModule<bool>");
        await Assert.That(programSource).Contains("AddModule<RepositoryDeliveryGateModule>()");
        await Assert.That(releaseSource).Contains(
            "[DependsOnAllModulesInheritingFrom(typeof(CompileCheckModule<>))]");
    }

    private static void WriteCachedPackage(string root, VerifiedPackage package, byte[] contents)
    {
        var directory = Path.Combine(root, package.Id.ToLowerInvariant(), package.Version.ToLowerInvariant());
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(
            Path.Combine(directory, $"{package.Id}.{package.Version}.nupkg".ToLowerInvariant()),
            contents);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TedToolkit.Orchestration.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private sealed class GateFixture : IDisposable
    {
        private GateFixture(string root)
        {
            Root = root;
        }

        internal string Root { get; }

        internal string[] ExpectedTests { get; } = ["Pipeline.Tests", "StateMachine.Tests",];

        internal TestReportSummary[] PassingReports { get; } =
        [
            new("Pipeline.Tests_results.trx", 2, 2),
            new("StateMachine.Tests_results.trx", 3, 3),
        ];

        internal static GateFixture Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "TedToolkit.Orchestration.Build.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            CreatePackage(root, "TedToolkit.Orchestration.Pipeline");
            CreatePackage(root, "TedToolkit.Orchestration.StateMachine");
            return new(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private static void CreatePackage(string root, string id)
        {
            var directory = Path.Combine(root, id + ".1.0.0");
            var runtime = Path.Combine(directory, "lib", "net10.0");
            var analyzers = Path.Combine(directory, "analyzers", "dotnet", "cs");
            Directory.CreateDirectory(runtime);
            Directory.CreateDirectory(analyzers);
            File.WriteAllText(Path.Combine(directory, id + ".nuspec"),
                $"<package><metadata><id>{id}</id><version>1.0.0</version></metadata></package>");
            File.WriteAllText(Path.Combine(runtime, id + ".dll"), "runtime");
            File.WriteAllText(Path.Combine(analyzers, id + ".Analyzer.dll"), "analyzer");
        }
    }
}
