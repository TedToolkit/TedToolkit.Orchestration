$project = Join-Path $PSScriptRoot 'build\TedToolkit.Orchestration.Build\TedToolkit.Orchestration.Build.csproj'

dotnet run --project $project --configuration Release
exit $LASTEXITCODE
