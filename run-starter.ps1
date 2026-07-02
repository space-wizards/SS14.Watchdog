$ErrorActionPreference = "Stop"
$env:ASPNETCORE_ENVIRONMENT = "Starter"
$env:DOTNET_ENVIRONMENT = "Starter"
dotnet run --no-launch-profile --project SS14.Watchdog/SS14.Watchdog.csproj
