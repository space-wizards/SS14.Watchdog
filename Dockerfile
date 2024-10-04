FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /build
ADD . .

RUN dotnet publish -c Release -r linux-x64 --no-self-contained

FROM mcr.microsoft.com/dotnet/sdk:8.0

# These are apparently required dependencies.
RUN apt update -y && \
    apt install -y git python3 python-is-python3

COPY --from=build /build/SS14.Watchdog/bin/Release/net8.0/linux-x64/publish /watchdog

WORKDIR /watchdog

# Both TCP and UDP traffic must be explicitly exposed on port 1212
EXPOSE 1212/tcp
EXPOSE 1212/udp
EXPOSE 8080/tcp

VOLUME ["/watchdog/instances"]

ENV DOTNET_ENVIRONMENT Production

ENTRYPOINT ["./SS14.Watchdog"]
