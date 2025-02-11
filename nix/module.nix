flake: { config, lib, pkgs, ... }:

with lib;

let
  cfg = config.services.space-station-14-watchdog;
  toml = pkgs.formats.toml { };
  appsettingsFile = pkgs.writeText "appsettings.yml"
    (builtins.toJSON (attrsets.recursiveUpdate appsettingsDefaults cfg.settings));
  appsettingsDefaults = {
    Serilog = {
      Using = [ "Serilog.Sinks.Console" "Serilog.Sinks.Loki" ];
      MinimumLevel = {
        Default = "Information";
        Override = {
          SS14 = "Information";
          Microsoft = "Warning";
          "Microsoft.Hosting.Lifetime" = "Information";
          "Microsoft.AspNetCore" = "Warning";
        };
      };
      WriteTo = [
        {
          Name = "Console";
          Args.OutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3} {SourceContext}] {Message:lj}{NewLine}{Exception}";
        }
      ];
    };
    BaseUrl = "http://127.0.0.1:5000/";
    AllowedHosts = "*";
  };
in
{

  options = {
    services.space-station-14-watchdog = {

      enable = mkOption {
        type = types.bool;
        default = false;
        description = lib.mkDoc ''
          If enabled, start the Space Station 14 Watchdog.
          The settings and instances will be loaded from and saved to
          {option}`services.space-station-14-watchdog.directory`.
        '';
      };

      package = mkOption {
        type = types.package;
        default = flake.packages.${pkgs.stdenv.hostPlatform.system}.space-station-14-watchdog;
        description = lib.mkDoc ''
          The package to use.
        '';
      };

      directory = mkOption {
        type = types.path;
        default = "/var/lib/space-station-14-watchdog";
        description = lib.mkDoc "Directory to store settings and instance data.";
      };

      settings = mkOption {
        type = (pkgs.formats.yaml { }).type;
        default = { };
        description = lib.mkDoc ''
          Watchdog settings. Mapped to appsettings.yml, see <https://docs.spacestation14.io/en/getting-started/hosting#ss14watchdog>.
        '';
      };

      openApiFirewall = mkOption {
        type = types.bool;
        default = false;
        description = lib.mkDoc "Whether to open ports in the firewall for the instances' API port.";
      };

      instances = mkOption {
        type = types.attrsOf (types.submodule ({ ... }: {
          options.configuration = mkOption {
            type = types.nullOr toml.type;
            default = null;
            description = lib.mkDoc "Configuration for this instance using CVars, see <https://docs.spacestation14.io/config-reference>.";
          };
        }));
        default = { };
        description = lib.mkDoc "Allows you to define per-instance settings declaratively.";
      };

    };
  };

  config = mkIf cfg.enable {

    users.users.ss14-watchdog = {
      description = "Space Station 14 Watchdog service user";
      group = "ss14-watchdog";
      home = cfg.directory;
      createHome = true;
      isSystemUser = true;
    };
    users.groups.ss14-watchdog = { };

    systemd.services.space-station-14-watchdog = {
      description = "Server Management System for Space Station 14.";
      wantedBy = [ "multi-user.target" ];
      after = [ "network-online.target" ];
      wants = [ "network-online.target" ];

      serviceConfig = {
        ExecStart = "${cfg.package}/bin/SS14.Watchdog";
        Restart = "always";
        User = config.users.users.ss14-watchdog.name;
        WorkingDirectory = cfg.directory;

        StandardOutput = "journal";
        StandardError = "journal";

        # Hardening
        CapabilityBoundingSet = [ "" ];
        DeviceAllow = [ "" ];
        LockPersonality = true;
        PrivateDevices = true;
        PrivateTmp = true;
        PrivateUsers = true;
        ProtectClock = true;
        ProtectHome = true;
        RestrictRealtime = true;
        RestrictSUIDSGID = true;
        SystemCallArchitectures = "native";
        UMask = "0077";
      };

      preStart = ''
        ln -sf ${appsettingsFile} appsettings.yml;
      '' + strings.concatStrings (attrsets.mapAttrsToList
        (n: v: ''
          mkdir -p instances/${n};
          rm -f instances/${n}/config.toml;
          ln -sf ${toml.generate "${n}-config.toml" v.configuration} instances/${n}/config.toml;
        '')
        (attrsets.filterAttrs (n: v: v.configuration != null) cfg.instances));
    };

    networking.firewall =
      let
        ports =
          if (cfg.settings ? Servers && cfg.settings.Servers ? Instances)
          then (map (x: x.ApiPort) (filter (x: x ? ApiPort) (attrValues cfg.settings.Servers.Instances)))
          else [ ];
      in
      mkIf cfg.openApiFirewall
        {
          allowedUDPPorts = ports;
          allowedTCPPorts = ports;
        };

  };

}
