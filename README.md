# SS14.Watchdog

SS14.Watchdog is SS14's server-hosting wrapper thing, similar to [TGS](https://github.com/tgstation/tgstation-server) for BYOND (but much simpler for the time being). It handles auto updates, monitoring, automatic restarts, and administration. We recommend you use this for proper deployments.

Documentation on how setup and use for SS14.Watchdog is [here](https://docs.spacestation14.io/en/getting-started/hosting#watchdog).

## Docker
To build and run Watchdog in a Docker container:

```sh
docker build -t ss14-watchdog .
docker run \
    -p 1212:1212/tcp \
    -p 1212:1212/udp \
    -p 8080:8080/tcp \
    -v /path/to/instances/:/watchdog/instances/ \
    -v /path/to/appsettings.yml:/watchdog/appsettings.yml \
    ss14-watchdog
```

To deploy with docker compose, edit `docker-compose.yml` to your liking, supplying your own appsettings.yml if needed, then run:

```sh
docker compose build
docker compose up -d
```
