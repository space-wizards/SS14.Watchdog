# SS14.Watchdog

SS14.Watchdog is SS14's server-hosting wrapper thing, similar to [TGS](https://github.com/tgstation/tgstation-server) for BYOND (but much simpler for the time being). It handles auto updates, monitoring, automatic restarts, and administration. We recommend you use this for proper deployments.

## Local server

`appsettings.yml` is intentionally minimal and mostly a dummy/default config.

For local hosting tests against a locally built game server, set `ASPNETCORE_ENVIRONMENT=Local` to load `appsettings.Local.yml`.
The local config uses `bin/instances` and the `Local` update provider.

## Starter server

Before running a starter server for the first time, open `SS14.Watchdog/appsettings.Starter.yml`, read the comments, and update the placeholder values.

Then start Watchdog with one of the starter scripts:

- Windows Command Prompt: `run-starter.bat`
- PowerShell: `./run-starter.ps1`
- Bash: `./run-starter.sh`

The starter config uses the `Git` update provider. The starter script will clone the target repository into SS14.Watchdog/instances/\<instance_name>

After it has run once you will see a config.toml that you can adjust to set the CVars to your liking.
For the full list of available CVars you will need to browse the development repository of your target server. Look for CCVars and CVars with Server flags.

Per-instance watchdog logs are written to `logs/<instance>/watchdog-*.log`.

Note that the instance_name is not the server name, it is just your name to refer to it. To update the server name set hostname in config.toml

## Update checks

Watchdog update checks are normally triggered by the game server between rounds. When the server reports that it is safe to update, Watchdog asks the configured update provider whether a newer version exists.
If an update is available, Watchdog tells the game server to shut down, applies the update after it exits, and relaunches the instance.

## Update providers

Each server instance chooses its own update provider with `Servers:Instances:<key>:UpdateType` and an optional `Updates` section. Different instances can use different providers.

### Manifest

Use `Manifest` when your publishing pipeline produces an SS14-style manifest. Watchdog fetches the manifest, chooses the newest build, validates, and applies it. This is recommended for long-term hosting.

Properties:

| Property | Required | Description |
| --- | --- | --- |
| `ManifestUrl` | Yes | URL of the SS14 build manifest JSON. |
| `Authentication` | No | Basic auth credentials used when fetching the manifest and referenced artifacts. |
| `Authentication:Username` | No | Basic auth username. |
| `Authentication:Password` | No | Basic auth password. |

```yml
Servers:
  Instances:
    example:
      UpdateType: Manifest
      Updates:
        ManifestUrl: "https://example.com/fork/example/manifest"
        # Authentication:
        #   Username: "user"
        #   Password: "pass"
```

### Jenkins

Use `Jenkins` for Jenkins publishing. Watchdog reads the job's last successful build and downloads `release/SS14.Server_<rid>.zip` from that build's artifacts.

Properties:

| Property | Required | Description |
| --- | --- | --- |
| `BaseUrl` | Yes | Base URL of the Jenkins instance, without the job path. |
| `JobName` | Yes | Jenkins job name to query for the last successful build. |

```yml
Servers:
  Instances:
    example:
      UpdateType: Jenkins
      Updates:
        BaseUrl: "https://builds.example.com/jenkins"
        JobName: "SS14 Content"
```

### Git

Use `Git` for local development or hosts that intentionally build from source on the deployment machine.
This is not recommended for long-term hosting but can be useful for smaller servers or test branches.

Properties:

| Property | Required | Description |
| --- | --- | --- |
| `BaseUrl` | Yes | Git repository URL. This is the source repository URL, not watchdog's root `BaseUrl`. |
| `Branch` | No | Git branch to fetch, reset to, and package. Defaults to `master`. |
| `HybridACZ` | No | Builds a hybrid ACZ package when `true`. Defaults to `true`; set `false` when watchdog should host client binaries from `/instances/<key>/binaries`. |

```yml
Servers:
  Instances:
    example:
      UpdateType: Git
      Updates:
        BaseUrl: "https://github.com/space-wizards/space-station-14.git"
        Branch: "stable"
        HybridACZ: true
```

### Local

Use `Local` when something else is managing files in the instance directory. Watchdog only tracks the configured `CurrentVersion`; it does not copy server files.

Properties:

| Property | Required | Description |
| --- | --- | --- |
| `CurrentVersion` | Yes | Version string watchdog records as active. Change this after replacing local files externally to make watchdog restart into the new revision. |

```yml
Servers:
  Instances:
    example:
      UpdateType: Local
      Updates:
        CurrentVersion: "local-build-1"
```

### Dummy

Use `Dummy` only for tests or manual experiments. It always reports an update and advances the recorded revision without copying files.

## Long-term hosting

For long-term public hosting, prefer a release pipeline that publishes builds and use `Manifest` updates instead of building from `Git` on the host.

Use a real database for production game server data. The generated starter `config.toml` defaults are suitable for getting a server running, but public or persistent servers should configure PostgreSQL in `config.toml` instead of relying on local SQLite files.
