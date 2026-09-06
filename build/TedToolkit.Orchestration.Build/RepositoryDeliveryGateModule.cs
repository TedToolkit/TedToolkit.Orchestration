// -----------------------------------------------------------------------
// <copyright file="RepositoryDeliveryGateModule.cs" company="TedToolkit">
// Copyright (c) TedToolkit. All rights reserved.
// Licensed under the LGPL-3.0 license. See COPYING, COPYING.LESSER file in the project root for full license information.
// </copyright>
// -----------------------------------------------------------------------

using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet;

using TedToolkit.ModularPipelines;
using TedToolkit.ModularPipelines.Constants;
using TedToolkit.ModularPipelines.Modules;

namespace TedToolkit.Orchestration.Build;

/// <summary>
/// Blocks repository delivery until all configured tests and packages are verified.
/// </summary>
/// <param name="parser">The shared TRX parser.</param>
/// <param name="files">The repository pipeline files.</param>
[DependsOn<TestModule>]
public sealed class RepositoryDeliveryGateModule(ITrxParser parser, PipelineFiles files) : CompileCheckModule<bool>
{
    /// <inheritdoc />
    protected override async Task<bool> ExecuteAsync(
        IModuleContext context,
        CancellationToken cancellationToken)
    {
        var failedFile = context.GetFailedFile();
        if (!failedFile.Exists)
        {
            throw new InvalidOperationException("The build result marker is missing.");
        }

        var failedProjects = await failedFile.ReadAsync(cancellationToken).ConfigureAwait(false);
        var trxFiles = await context.GetModule<TestModule>()
            .GetResultValueAsync<TestModule, FileInfo[]>()
            .ConfigureAwait(false);

        var reports = await Task.WhenAll(trxFiles.Select(async file =>
        {
            var result = parser.ParseTrxContents(await File.ReadAllTextAsync(file.FullName, cancellationToken)
                .ConfigureAwait(false));
            return new TestReportSummary(
                file.Name,
                result.ResultSummary.Counters.Executed,
                result.ResultSummary.Counters.Passed);
        })).ConfigureAwait(false);

        var packages = DeliveryGateValidator.Validate(
            failedProjects,
            files.TestFiles.Select(file => Path.GetFileNameWithoutExtension(file.Name)).ToArray(),
            reports,
            context.GetNugetFolder().Path);

        await PackageConsumerVerifier.VerifyAsync(context, packages, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
