#!/usr/bin/env sh

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

exec dotnet run \
    --project "$script_dir/build/TedToolkit.Orchestration.Build/TedToolkit.Orchestration.Build.csproj" \
    --configuration Release
