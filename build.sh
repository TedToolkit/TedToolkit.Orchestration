#!/usr/bin/env sh

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

exec dotnet run \
    --project "$script_dir/Build/Build.csproj" \
    --configuration Release
