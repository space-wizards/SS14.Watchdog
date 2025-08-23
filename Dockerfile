FROM debian:bookworm-slim as build

RUN apt update && apt install -y wget && \
    wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    rm packages-microsoft-prod.deb && \
    apt update && apt install -y dotnet-sdk-9.0 && \
    apt clean && rm -rf /var/lib/apt/lists/*

WORKDIR /build
COPY . .

RUN dotnet publish -c Release -r linux-x64 --no-self-contained

FROM debian:bookworm-slim as runtime

RUN apt update && apt install -y wget && \
    wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    rm packages-microsoft-prod.deb && \
    apt update && apt install -y aspnetcore-runtime-9.0 && \
    apt clean && rm -rf /var/lib/apt/lists/*

COPY --from=build /build/SS14.Watchdog/bin/Release/net9.0/linux-x64/publish /watchdog
WORKDIR /watchdog

RUN chmod +x ./SS14.Watchdog

EXPOSE 5000
CMD ["./SS14.Watchdog"]