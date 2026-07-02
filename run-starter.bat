@echo off
setlocal
set ASPNETCORE_ENVIRONMENT=Starter
set DOTNET_ENVIRONMENT=Starter
dotnet run --no-launch-profile --project SS14.Watchdog\SS14.Watchdog.csproj
