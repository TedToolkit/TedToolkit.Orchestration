$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'temp'))
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryRoot 'pipeline-trimming-verification'))
$packageCache = [IO.Path]::GetFullPath((Join-Path $temporaryRoot 'pipeline-package-consumer-packages'))
$allowedPrefix = $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $publishDirectory.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not $packageCache.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Verification outputs must stay below the repository temp directory."
}

$project = Join-Path $repositoryRoot 'tests\TedToolkit.Orchestration.Pipeline.PackageConsumer\TedToolkit.Orchestration.Pipeline.PackageConsumer.csproj'
$runtimeProject = Join-Path $repositoryRoot 'src\TedToolkit.Orchestration.Pipeline\TedToolkit.Orchestration.Pipeline.csproj'
$packageDirectory = Join-Path $repositoryRoot 'src\TedToolkit.Orchestration.Pipeline\bin\Release'
$nugetConfig = Join-Path $repositoryRoot 'tests\TedToolkit.Orchestration.Pipeline.PackageConsumer\NuGet.config'
$assembly = Join-Path $publishDirectory 'TedToolkit.Orchestration.Pipeline.PackageConsumer.dll'
$executable = Join-Path $publishDirectory 'TedToolkit.Orchestration.Pipeline.PackageConsumer.exe'
$publishArguments = @(
    'publish', $project,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $publishDirectory,
    '--packages', $packageCache,
    '--configfile', $nugetConfig
)

foreach ($path in @($publishDirectory, $packageCache)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

try {
    & dotnet pack $runtimeProject --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Pipeline package creation failed with exit code $LASTEXITCODE."
    }

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Trimmed Pipeline publish failed with exit code $LASTEXITCODE."
    }

    Add-Type -AssemblyName System.Reflection.Metadata
    $stream = [IO.File]::OpenRead($assembly)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            $configurationNames = @(
                foreach ($handle in $metadata.MethodDefinitions) {
                    $name = $metadata.GetString($metadata.GetMethodDefinition($handle).Name)
                    if ($name -in 'Configuration', 'Configure') { $name }
                }
                foreach ($handle in $metadata.MemberReferences) {
                    $name = $metadata.GetString($metadata.GetMemberReference($handle).Name)
                    if ($name -in 'Configuration', 'Configure') { $name }
                }
            )
            if ($configurationNames.Count -ne 0) {
                throw "Trimmed metadata still contains Configuration/Configure: $($configurationNames -join ', ')."
            }
        }
        finally {
            $pe.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    & $executable
    if ($LASTEXITCODE -ne 0) {
        throw "The trimmed Pipeline executable failed with exit code $LASTEXITCODE."
    }
}
finally {
    foreach ($path in @($publishDirectory, $packageCache)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
}
