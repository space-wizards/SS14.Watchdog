{ flake, buildFHSEnv, buildDotnetModule, dotnetCorePackages, git, python3, zstd, zlib }:
let
  rev = flake.rev or "dirty";
  dotnet-sdk = with dotnetCorePackages;
    combinePackages [ sdk_8_0 aspnetcore_8_0 sdk_9_0 aspnetcore_9_0 ];
  watchdog = buildDotnetModule {
    inherit dotnet-sdk;
    name = "space-station-14-watchdog-${rev}";

    src = ../.;

    projectFile = [ "SS14.Watchdog/SS14.Watchdog.csproj" ];

    buildType = "Release";
    selfContainedBuild = false;

    # Generated using "fetch-deps" flake app output.
    nugetDeps = ./deps.json;

    runtimeDeps = [ git python3 zstd zlib ];

    dotnet-runtime = with dotnetCorePackages;
      combinePackages [ runtime_8_0 aspnetcore_8_0 runtime_9_0 aspnetcore_9_0 ];

    executables = [ "SS14.Watchdog" ];
  };
in
buildFHSEnv {
  name = "SS14.Watchdog";

  targetPkgs = pkgs: [ watchdog git python3 dotnet-sdk zstd ];

  runScript = "SS14.Watchdog";

  passthru = watchdog.passthru // {
    name = "space-station-14-watchdog-wrapped-${rev}";
  };
}
