#!/usr/bin/env bash
set -euo pipefail

export ASPNETCORE_ENVIRONMENT=Starter
export DOTNET_ENVIRONMENT=Starter

dotnet run --no-launch-profile --project SS14.Watchdog/SS14.Watchdog.csproj
