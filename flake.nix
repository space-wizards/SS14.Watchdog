{
  description =
    "Flake providing a package and NixOS module for the Space Station 14 Watchdog.";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/release-24.11";

  outputs = { self, nixpkgs }:
    let
      nixos-lib = import (nixpkgs + "/nixos/lib") { };
      forAllSystems = function:
        nixpkgs.lib.genAttrs [ "x86_64-linux" "aarch64-linux" ]
          (system: function (import nixpkgs { inherit system; }));
    in
    {

      packages = forAllSystems (pkgs: {
        default = self.packages.${pkgs.system}.space-station-14-watchdog;
        space-station-14-watchdog =
          pkgs.callPackage ./nix/package.nix { flake = self; };
        vm-test = nixos-lib.runTest {
          name = "space-station-14-watchdog";
          imports = [ ./nix/test.nix ];
          hostPkgs = pkgs;
          extraBaseModules = { imports = [ self.nixosModules.space-station-14-watchdog ]; };
        };
      });

      overlays = {
        default = self.overlays.space-station-14-watchdog;
        space-station-14-watchdog = final: prev: {
          space-station-14-watchdog =
            self.packages.${prev.system}.space-station-14-watchdog;
        };
      };

      apps = forAllSystems (pkgs: {
        default = self.apps.${pkgs.system}.space-station-14-watchdog;
        space-station-14-watchdog = {
          type = "app";
          program = "${
              self.packages.${pkgs.system}.space-station-14-watchdog
            }/bin/SS14.Watchdog";
        };
        fetch-deps = {
          type = "app";
          program = toString
            self.packages.${pkgs.system}.space-station-14-watchdog.passthru.fetch-deps;
        };
        vm-test = {
          type = "app";
          program = "${self.packages.${pkgs.system}.vm-test.driver}/bin/nixos-test-driver";
        };
        vm-test-interactive = {
          type = "app";
          program = "${self.packages.${pkgs.system}.vm-test.driverInteractive}/bin/nixos-test-driver";
        };
      });

      nixosModules = {
        default = self.nixosModules.space-station-14-watchdog;
        space-station-14-watchdog = import ./nix/module.nix self;
      };

      formatter = forAllSystems (pkgs: pkgs.nixpkgs-fmt);

    };
}
