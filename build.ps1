$project = Join-Path $PSScriptRoot 'Build\Build.csproj'

dotnet run --project $project --configuration Release
exit $LASTEXITCODE
