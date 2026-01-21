FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY *.sln .
COPY SS14.Watchdog/. ./SS14.Watchdog/
COPY SS14.Watchdog.Tests/. ./SS14.Watchdog.Tests/

RUN dotnet publish -c Release -r linux-x64 --no-self-contained -o /publish
RUN dotnet test

FROM build AS dev
WORKDIR /watchdog
COPY --from=build /publish .
RUN chmod +x ./SS14.Watchdog
CMD ["./SS14.Watchdog"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /watchdog
COPY --from=build /publish .
RUN chmod +x ./SS14.Watchdog
CMD ["./SS14.Watchdog"]