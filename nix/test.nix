{
  nodes.machine = { ... }:
    {
      services.space-station-14-watchdog = {
        enable = true;
        openApiFirewall = true;
        settings.Servers.Instances.Test = {
          Name = "Test Instance";
          ApiToken = "1234";
          ApiPort = 1212;
          TimeoutSeconds = 240;
          UpdateType = "Manifest";
          Updates.ManifestUrl = "https://wizards.cdn.spacestation14.com/fork/wizards/manifest";
        };
        instances.Test.configuration = {
          status = {
            enabled = true;
            bind = "*:1212";
          };
        };
      };
    };

  testScript = ''
    machine.start()
    machine.wait_for_unit("space-station-14-watchdog.service")
    machine.wait_for_open_port(1212)
    machine.wait_until_succeeds("curl http://127.0.0.1:1212/status");
  '';
}
