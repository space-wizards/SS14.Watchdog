# -*- coding: utf-8 -*-
# vim: ft=Dockerfile

ARG DOTNET_VERSION=10.0
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-noble
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled-composite
ARG BUSYBOX_IMAGE=busybox:1.37.0-uclibc

### busybox for sh access
FROM ${BUSYBOX_IMAGE} AS busybox
LABEL maintainer="mindhunter86 <mindhunter86@vkom.cc>"
RUN mkdir -p /bb/bin \
  && cp /bin/busybox /bb/bin/busybox \
  && ln -s /bin/busybox /bb/bin/sh \
  && ln -s /bin/busybox /bb/bin/taskset \
  && ln -s /bin/busybox /bb/bin/chrt \
  && ln -s /bin/busybox /bb/bin/awk \
  && ln -s /bin/busybox /bb/bin/tr

### NET10 building
FROM ${SDK_IMAGE} as build
LABEL maintainer="mindhunter86 <mindhunter86@vkom.cc>"

USER 0:0
WORKDIR /usr/sources/ss14.watchdog

# hadolint/hadolint - DL4006
SHELL ["/bin/bash", "-o", "pipefail", "-c"]

COPY SS14.Watchdog/SS14.Watchdog.csproj SS14.Watchdog/
RUN dotnet restore -r linux-x64 SS14.Watchdog/SS14.Watchdog.csproj

COPY . .
RUN dotnet publish SS14.Watchdog/SS14.Watchdog.csproj \
  -c Release \
  -r linux-x64 \
  -o ./dist \
  --no-restore \
  /p:SelfContained=false \
  /p:UseAppHost=false \
  /p:DebugType=None \
  /p:DebugSymbols=false \
  /p:InvariantGlobalization=true

### NET10 distroless : github.com/dotnet/dotnet-docker
FROM ${RUNTIME_IMAGE} as application
LABEL maintainer="mindhunter86 <mindhunter86@vkom.cc>"

USER 0:${APP_UID}
WORKDIR /data/ss14/watchdog

# hadolint/hadolint - DL4006
SHELL ["/bin/sh", "-eo", "pipefail", "-c"]

# common production-ready env
ENV TZ=Etc/UTC \
  DOTNET_ENVIRONMENT=Production \
  ASPNETCORE_ENVIRONMENT=Production \
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
  DOTNET_EnableDiagnostics=0 \
  PATH="/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"

# small tunning for SS14
ENV DOTNET_CLI_HOME="/tmp" \
  GLIBC_TUNABLES="glibc.rtld.dynamic_sort=1" \
  DOTNET_TieredPGO="1" \
  DOTNET_TC_QuickJitForLoops="1" \
  DOTNET_ReadyToRun="0" \
  ROBUST_NUMERICS_AVX="true" \
  DOTNET_GCHeapCount="8" \
  DOTNET_gcConcurrent="1" \
  DOTNET_PROCESSOR_COUNT="16"

# sh is a requirement for custom sh scripts like CPU pinning utilities
# also we need crond, so busybox looks very good here
COPY --from=busybox /bin/busybox /bin/busybox
COPY --from=busybox /bb/bin/ /bin/

# copy TZdata
COPY --from=build /usr/share/zoneinfo/Etc/UTC /etc/localtime

# application builded data copy
COPY --from=build --chown=0:${APP_UID} /usr/sources/ss14.watchdog/dist/ ./

RUN busybox rm -vf appsettings.yml \
  && busybox chmod o+w /data/ss14/watchdog \
  && busybox ln -s /data/ss14/instances instances \
  && busybox ln -s /data/ss14/configs/watchdog.appsettings.yml appsettings.yml

USER ${APP_UID}
ENTRYPOINT ["dotnet", "SS14.Watchdog.dll"]
CMD ["--help"]

