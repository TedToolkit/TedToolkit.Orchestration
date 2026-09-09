$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'temp'))
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryRoot 'package-compatibility-publish'))
$packageCache = [IO.Path]::GetFullPath((Join-Path $temporaryRoot 'package-compatibility-cache'))
$allowedPrefix = $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($path in @($publishDirectory, $packageCache)) {
    if (-not $path.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Verification outputs must stay below the repository temp directory."
    }
}

$frameworks = @('netstandard2.0', 'netstandard2.1', 'net472', 'net48', 'net6.0', 'net7.0', 'net8.0', 'net9.0', 'net10.0')
$consumer = Join-Path $repositoryRoot 'tests\TedToolkit.Orchestration.PackageConsumer\TedToolkit.Orchestration.PackageConsumer.csproj'
$nugetConfig = Join-Path $repositoryRoot 'tests\TedToolkit.Orchestration.PackageConsumer\NuGet.config'
$pipelineProject = Join-Path $repositoryRoot 'src\TedToolkit.Orchestration.Pipeline\TedToolkit.Orchestration.Pipeline.csproj'
$stateMachineProject = Join-Path $repositoryRoot 'src\TedToolkit.Orchestration.StateMachine\TedToolkit.Orchestration.StateMachine.csproj'
$pipelinePackage = Join-Path $repositoryRoot 'src\TedToolkit.Orchestration.Pipeline\bin\Release\TedToolkit.Orchestration.Pipeline.1.0.0.nupkg'
$stateMachinePackage = Join-Path $repositoryRoot 'src\TedToolkit.Orchestration.StateMachine\bin\Release\TedToolkit.Orchestration.StateMachine.1.0.0.nupkg'

function Get-PackageEntries([string]$path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try { return @($archive.Entries.FullName) }
    finally { $archive.Dispose() }
}

function Assert-Package([string]$path, [string]$assembly, [string[]]$analyzers) {
    $entries = Get-PackageEntries $path
    foreach ($framework in $frameworks) {
        $expected = "lib/$framework/$assembly.dll"
        if ($expected -notin $entries) { throw "$path is missing $expected." }
    }
    foreach ($analyzer in $analyzers) {
        $expected = "analyzers/dotnet/cs/$analyzer"
        if ($expected -notin $entries) { throw "$path is missing $expected." }
    }
}

function Get-PublicCompilerSupportTypes([string]$path) {
    Add-Type -AssemblyName System.Reflection.Metadata
    $stream = [IO.File]::OpenRead($path)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            foreach ($handle in $metadata.TypeDefinitions) {
                $type = $metadata.GetTypeDefinition($handle)
                if (($type.Attributes -band [Reflection.TypeAttributes]::VisibilityMask) -ne [Reflection.TypeAttributes]::Public) { continue }
                $name = $metadata.GetString($type.Name)
                if ($name -in @('IsExternalInit', 'RequiredMemberAttribute', 'CompilerFeatureRequiredAttribute')) {
                    $namespace = $metadata.GetString($type.Namespace)
                    "$namespace.$name"
                }
            }
        }
        finally { $pe.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Assert-Same([string[]]$actual, [string[]]$expected, [string]$scope) {
    $actualKey = (@($actual) | Sort-Object) -join '|'
    $expectedKey = (@($expected) | Sort-Object) -join '|'
    if ($actualKey -ne $expectedKey) { throw "$scope has an unexpected compiler-support type set: $($actual -join ', ')." }
}

foreach ($path in @($publishDirectory, $packageCache)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

try {
    & dotnet build $pipelineProject --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Pipeline package build failed with exit code $LASTEXITCODE." }
    & dotnet build $stateMachineProject --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "StateMachine package build failed with exit code $LASTEXITCODE." }

    Assert-Package $pipelinePackage 'TedToolkit.Orchestration.Pipeline' @(
        'TedToolkit.Orchestration.Pipeline.Analyzer.dll', 'TedToolkit.RoslynHelper.dll', 'ZString.dll', 'System.Memory.dll')
    Assert-Package $stateMachinePackage 'TedToolkit.Orchestration.StateMachine' @(
        'TedToolkit.Orchestration.StateMachine.Analyzer.dll')

    $initType = 'System.Runtime.CompilerServices.IsExternalInit'
    $requiredTypes = @(
        'System.Runtime.CompilerServices.RequiredMemberAttribute',
        'System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute')
    foreach ($framework in $frameworks) {
        $pipelineAssembly = Join-Path $repositoryRoot "src\TedToolkit.Orchestration.Pipeline\bin\Release\$framework\TedToolkit.Orchestration.Pipeline.dll"
        $stateMachineAssembly = Join-Path $repositoryRoot "src\TedToolkit.Orchestration.StateMachine\bin\Release\$framework\TedToolkit.Orchestration.StateMachine.dll"
        $expected = @()
        if ($framework -in @('netstandard2.0', 'netstandard2.1', 'net472', 'net48')) { $expected += $initType }
        if ($framework -in @('netstandard2.0', 'netstandard2.1', 'net472', 'net48', 'net6.0')) { $expected += $requiredTypes }
        Assert-Same @(Get-PublicCompilerSupportTypes $pipelineAssembly) $expected "Pipeline $framework"
        Assert-Same @(Get-PublicCompilerSupportTypes $stateMachineAssembly) @() "StateMachine $framework"
    }

    & dotnet restore $consumer --packages $packageCache --configfile $nugetConfig --force-evaluate
    if ($LASTEXITCODE -ne 0) { throw "Package consumer restore failed with exit code $LASTEXITCODE." }
    & dotnet build $consumer --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Package consumer matrix build failed with exit code $LASTEXITCODE." }

    $assets = Get-Content -Raw -LiteralPath (Join-Path (Split-Path $consumer) 'obj\project.assets.json') | ConvertFrom-Json -AsHashtable
    foreach ($framework in $frameworks) {
        foreach ($package in @('TedToolkit.Orchestration.Pipeline', 'TedToolkit.Orchestration.StateMachine')) {
            $library = $assets.targets[$framework]["$package/1.0.0"]
            $expected = "lib/$framework/$package.dll"
            if ($null -eq $library -or $expected -notin @($library.compile.Keys)) {
                throw "$framework did not resolve $expected."
            }
        }
    }

    & dotnet publish $consumer --framework net10.0 --configuration Release --runtime win-x64 --self-contained true `
        --output $publishDirectory --packages $packageCache --configfile $nugetConfig
    if ($LASTEXITCODE -ne 0) { throw "Trimmed package consumer publish failed with exit code $LASTEXITCODE." }

    $assembly = Join-Path $publishDirectory 'TedToolkit.Orchestration.PackageConsumer.dll'
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
        finally { $pe.Dispose() }
    }
    finally { $stream.Dispose() }

    & (Join-Path $publishDirectory 'TedToolkit.Orchestration.PackageConsumer.exe')
    if ($LASTEXITCODE -ne 0) { throw "The trimmed package consumer failed with exit code $LASTEXITCODE." }
}
finally {
    foreach ($path in @($publishDirectory, $packageCache)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
}
