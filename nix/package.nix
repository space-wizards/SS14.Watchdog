{ flake, buildFHSEnv, buildDotnetModule, dotnetCorePackages, git }:
let
  rev = flake.rev or "dirty";
  watchdog = buildDotnetModule {
    name = "space-station-14-watchdog-${rev}";

    src = ../.;

    projectFile = [ "SS14.Watchdog/SS14.Watchdog.csproj" ];

    buildType = "Release";
    selfContainedBuild = false;

    # Generated using "fetch-deps" flake app output.
    nugetDeps = ./deps.nix;

    runtimeDeps = [ git ];

    dotnet-sdk = with dotnetCorePackages;
      combinePackages [ sdk_7_0 aspnetcore_7_0 ];
    dotnet-runtime = with dotnetCorePackages;
      combinePackages [ runtime_7_0 aspnetcore_7_0 ];

    executables = [ "SS14.Watchdog" ];
  };
in buildFHSEnv {
  name = "SS14.Watchdog";

  targetPkgs = pkgs: [ watchdog ];

  runScript = "SS14.Watchdog";

  passthru = watchdog.passthru // {
    name = "space-station-14-watchdog-wrapped-${rev}";
  };
}