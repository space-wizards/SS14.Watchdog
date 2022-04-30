# SS14.Watchdog 

SS14.Watchdog (codename Ian) is SS14's server-hosting wrapper thing, similar to [TGS](https://github.com/tgstation/tgstation-server) for BYOND (but much simpler for the time being). It handles auto updates, monitoring, automatic restarts, and administration. We recommend you use this for proper deployments.

Documentation on how setup and use for SS14.Watchdog is [here](https://docs.spacestation14.io/en/getting-started/hosting#ss14watchdog).

## Docker
For convenience, a docker image is provided, with an example use below:
```sh
docker run \
--name="ss14" \
-p 5000:5000 \
-p 1212:1212 \
-p 1212:1212/udp \
-v /path/to/appsettings.yml:/app/appsettings.yml \
-v /path/to/instances/folder:/app/instances \
cadynz/ss14-watchdog
```
