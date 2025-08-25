FROM mcr.microsoft.com/dotnet/sdk:9.0 as build

WORKDIR /build

COPY *.sln .
COPY SS14.Watchdog/. ./SS14.Watchdog/
COPY SS14.Watchdog.Tests/. ./SS14.Watchdog.Tests/

RUN dotnet publish -c Release -r linux-x64 --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:9.0 as runtime

WORKDIR /watchdog

COPY --from=build /build/SS14.Watchdog/bin/Release/net9.0/linux-x64/publish .

RUN chmod +x ./SS14.Watchdog

CMD ["./SS14.Watchdog"]
