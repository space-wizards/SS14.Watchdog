# -*- coding: utf-8 -*-
# vim: ft=Dockerfile

ARG DOTNET_VERSION=10.0

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-noble
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled-composite-extra
ARG BUSYBOX_IMAGE=busybox:1.37.0-uclibc
ARG FFMPEG_IMAGE=ubuntu:24.04

ARG FFMPEG_VERSION=8.1.1
ARG FFMPEG_BUILD_IMAGE=ubuntu:24.04

### busybox for sh access
FROM ${BUSYBOX_IMAGE} AS busybox
LABEL maintainer="mindhunter86 <mindhunter86@vkom.cc>"
RUN mkdir -p /bb/bin \
  && cp /bin/busybox /bb/bin/busybox \
  && ln -s /bin/busybox /bb/bin/sh \
  && ln -s /bin/busybox /bb/bin/taskset \
  && ln -s /bin/busybox /bb/bin/chrt \
  && ln -s /bin/busybox /bb/bin/awk \
  && ln -s /bin/busybox /bb/bin/tr \
  && ln -s /bin/busybox /bb/bin/sleep \
  && ln -s /bin/busybox /bb/bin/grep

### FFMPEG BUILD
# FROM ${FFMPEG_IMAGE} AS ffmpeg
# USER 0:0

# ENV DEBIAN_FRONTEND=noninteractive

# RUN apt-get update && \
#     apt-get install -y --no-install-recommends ffmpeg ca-certificates && \
#     rm -rf /var/lib/apt/lists/*

# # Собираем только runtime closure для ffmpeg/ffprobe.
# RUN mkdir -p /ffmpeg-root/usr/local/bin /ffmpeg-root/usr/local/lib/ffmpeg && \
#     cp -L /usr/bin/ffmpeg /ffmpeg-root/usr/local/bin/ffmpeg && \
#     cp -L /usr/bin/ffprobe /ffmpeg-root/usr/local/bin/ffprobe && \
#     for bin in /usr/bin/ffmpeg /usr/bin/ffprobe; do \
#       ldd "$bin" \
#         | awk '/=> \// { print $3 } /^\// { print $1 }' \
#         | sort -u; \
#     done \
#       | while read -r lib; do \
#           cp -L "$lib" /ffmpeg-root/usr/local/lib/ffmpeg/; \
#         done

# # Диагностика: эта библиотека у тебя сейчас отсутствует в final image.
# RUN find /ffmpeg-root -name 'libavdevice.so*' -o -name 'libavcodec.so*' -o -name 'libavformat.so*'
# # Проверка внутри stage.
# RUN LD_LIBRARY_PATH=/ffmpeg-root/usr/local/lib/ffmpeg \
#     /ffmpeg-root/usr/local/bin/ffmpeg -hide_banner -version






FROM ${FFMPEG_BUILD_IMAGE} AS ffmpeg
USER 0:0

ENV DEBIAN_FRONTEND=noninteractive

ARG FFMPEG_VERSION

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
      ca-certificates \
      curl \
      xz-utils \
      build-essential \
      pkg-config \
      nasm \
      pax-utils \
      libvorbis-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /build

RUN curl -fsSLO "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz" && \
    tar -xf "ffmpeg-${FFMPEG_VERSION}.tar.xz"

WORKDIR /build/ffmpeg-${FFMPEG_VERSION}

RUN test -f libavfilter/af_acrusher.c && echo "acrusher source exists" || echo "acrusher source missing"
RUN grep -R "acrusher" -n libavfilter | head -50 || true

RUN ./configure \
      --prefix=/opt/ffmpeg \
      --disable-everything \
      --disable-autodetect \
      --disable-doc \
      --disable-debug \
      --disable-ffplay \
      --disable-ffprobe \
      --enable-ffmpeg \
      --disable-avdevice \
      --disable-swscale \
      --enable-avcodec \
      --enable-avformat \
      --enable-avfilter \
      --enable-swresample \
      --enable-protocol=unix \
      --enable-muxer=ogg \
      --enable-demuxer=ogg \
      --enable-parser=vorbis \
      --enable-parser=opus \
      --enable-parser=flac \
      --enable-decoder=vorbis \
      --enable-decoder=opus \
      --enable-decoder=flac \
      --enable-libvorbis \
      --enable-encoder=libvorbis \
      --enable-filter=highpass \
      --enable-filter=lowpass \
      --enable-filter=acrusher \
      --enable-filter=aresample \
      --enable-filter=aformat \
      --enable-filter=anull \
      --enable-small \
      --enable-static \
      --disable-shared && \
    grep -C 8 -E 'acrusher_filter|ACRUSHER' ffbuild/config.log ffbuild/config.mak || true \
    make -j"$(nproc)" && \
    make install && \
    strip /opt/ffmpeg/bin/ffmpeg

RUN grep -E 'ACRUSHER|acrusher' ffbuild/config.mak ffbuild/config.log || true
RUN grep -E '^CONFIG_(ACRUSHER|HIGHPASS|LOWPASS)_FILTER=yes' ffbuild/config.mak
RUN grep -C 8 -E 'acrusher_filter|ACRUSHER' ffbuild/config.log ffbuild/config.mak || true
RUN /opt/ffmpeg/bin/ffmpeg -hide_banner -filters | grep -E 'acrusher|highpass|lowpass' || true

RUN mkdir -p /ffmpeg-root/usr/local/bin /ffmpeg-root/usr/local/lib/ffmpeg && \
    cp -L /opt/ffmpeg/bin/ffmpeg /ffmpeg-root/usr/local/bin/ffmpeg

RUN lddtree -l /opt/ffmpeg/bin/ffmpeg \
      | sort -u \
      | grep '^/' \
      | while read -r path; do \
          if [ -f "$path" ]; then \
            base="$(basename "$path")"; \
            case "$base" in \
              ffmpeg|ld-linux*) ;; \
              *) cp -L "$path" "/ffmpeg-root/usr/local/lib/ffmpeg/$base" ;; \
            esac; \
          fi; \
        done

RUN LD_LIBRARY_PATH=/ffmpeg-root/usr/local/lib/ffmpeg \
    /ffmpeg-root/usr/local/bin/ffmpeg -hide_banner -version



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
  /p:DebugSymbols=false

# libz нужен MonoPosixHelper.
RUN mkdir -p /native-libs && \
    zlib="$(find /lib /usr/lib -name libz.so.1 -print -quit)" && \
    test -n "$zlib" && \
    cp -L "$zlib" /native-libs/libz.so.1

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
  DOTNET_EnableDiagnostics=0 \
  LD_LIBRARY_PATH="/lib/x86_64-linux-gnu:/usr/lib/x86_64-linux-gnu:/usr/lib:/usr/local/lib:/usr/lib/ffmpeg" \
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

COPY --chown=0:0 --from=build /native-libs/libz.so.1 /usr/lib/libz.so.1

# COPY --chown=0:0 --from=ffmpeg /ffmpeg-root/usr/local/bin/ /usr/bin/
# COPY --chown=0:0 --from=ffmpeg /ffmpeg-root/usr/local/lib/ffmpeg/ /usr/lib/ffmpeg/

COPY --chown=0:0 --from=ffmpeg /ffmpeg-root/usr/local/bin/ffmpeg /usr/bin/ffmpeg
COPY --chown=0:0 --from=ffmpeg /ffmpeg-root/usr/local/lib/ffmpeg/ /usr/lib/ffmpeg/

RUN ffmpeg -hide_banner -version && \
    ffmpeg -hide_banner -protocols | grep -E '^[[:space:]]*unix$' && \
    ffmpeg -hide_banner -muxers | grep -E '[[:space:]]E[[:space:]]+ogg' && \
    ffmpeg -hide_banner -filters | grep highpass && \
    ffmpeg -hide_banner -filters | grep lowpass && \
    ffmpeg -hide_banner -filters | grep acrusher

# Проверяем, что ffmpeg стартует уже в final image.
# RUN ffmpeg -hide_banner -version && ffprobe -hide_banner -version

# application builded data copy
COPY --from=build --chown=0:${APP_UID} /usr/sources/ss14.watchdog/dist/ ./

RUN busybox rm -vf appsettings.yml \
  && busybox chmod o+w /data/ss14/watchdog \
  && busybox ln -s /data/ss14/instances instances \
  && busybox ln -s /data/ss14/configs/watchdog.appsettings.yml appsettings.yml

USER ${APP_UID}
ENTRYPOINT ["dotnet", "SS14.Watchdog.dll"]
CMD ["--help"]

