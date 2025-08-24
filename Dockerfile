FROM debian:bookworm-slim as build

WORKDIR /build

RUN apt update && apt install -y wget && \
    wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    apt update && apt install -y dotnet-sdk-9.0 && \
    apt clean && rm -rf /var/lib/apt/lists/*

COPY *.sln .
COPY SS14.Watchdog/. ./SS14.Watchdog/
COPY SS14.Watchdog.Tests/. ./SS14.Watchdog.Tests/

RUN dotnet publish -c Release -r linux-x64 --no-self-contained

FROM debian:bookworm-slim as runtime

RUN mkdir /tmp/packages

COPY --from=build /build/packages-microsoft-prod.deb /tmp/packages/packages-microsoft-prod.deb

WORKDIR /tmp/packages

RUN apt update && apt install -y ca-certificates && \
    dpkg -i packages-microsoft-prod.deb && \
    apt update && apt install -y aspnetcore-runtime-9.0 && \
    apt clean && rm -rf /var/lib/apt/lists/*

WORKDIR /watchdog

COPY --from=build /build/SS14.Watchdog/bin/Release/net9.0/linux-x64/publish .

RUN rm -rf /tmp/packages

RUN chmod +x ./SS14.Watchdog

EXPOSE 5000
CMD ["./SS14.Watchdog"]