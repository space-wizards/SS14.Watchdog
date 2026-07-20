# ===========================
# Build Stage
# ===========================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
ARG BUILD_CONFIGURATION=Release

COPY . .
WORKDIR /src/SS14.Watchdog

# Restore and publish the project in Release mode
RUN dotnet publish SS14.Watchdog.csproj -c $BUILD_CONFIGURATION -r linux-x64 --no-self-contained -o /app/publish

# ===========================
# Runtime Stage (minimal)
# ===========================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

# Copy only the published binaries
COPY --from=build /app/publish .

RUN apt-get update -y
RUN apt-get install libkrb5-3 libgssapi-krb5-2 -y

EXPOSE 1212
EXPOSE 5000

# Run the application
ENTRYPOINT ["dotnet", "SS14.Watchdog.dll"]
