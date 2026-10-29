# The game's services as images, one target each (docker-compose.yml builds them all):
#
#     docker build -f docker/Dockerfile --target account .   # the account server (source/App)
#     docker build -f docker/Dockerfile --target world .     # the world proxy and world servers
#     docker build -f docker/Dockerfile --target gateway .   # the gateway, serving the web client
#     docker build -f docker/Dockerfile --target discord .   # the Discord bot (tools/discord)
#
# Built from the repository's root (see .dockerignore). Nothing secret goes into an image: the
# settings come from the environment when a container starts (docker/configure.py), and the
# Discord bot's token from an env file given to its container. The live VPS doesn't run these:
# it runs the same builds under systemd (deploy/deploy.py); see SETUP-LOCAL.md, "Docker".

# --- the .NET servers ------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS dotnet-build
WORKDIR /src
# the projects first, so a code change doesn't download the packages again
COPY source/Shared/Shared.csproj source/Shared/
COPY source/App/App.csproj source/App/
COPY source/WorldServer/WorldServer.csproj source/WorldServer/
COPY source/Redis/Redis.csproj source/Redis/
RUN dotnet restore source/App/App.csproj -r linux-x64 \
 && dotnet restore source/WorldServer/WorldServer.csproj -r linux-x64
COPY source/ source/
# Debug, as deploy/deploy.py builds for the VPS (Optimize is on for WorldServer and Shared)
RUN dotnet publish source/App/App.csproj -c Debug -r linux-x64 --self-contained false --no-restore \
        -o /out/app -v q --nologo \
 && dotnet publish source/WorldServer/WorldServer.csproj -c Debug -r linux-x64 --self-contained false --no-restore \
        -o /out/worldserver -v q --nologo

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS dotnet-base
# tini: signals reach the server, and the processes it leaves are reaped
RUN apt-get update -q && apt-get install -y -q --no-install-recommends python3 tini \
 && rm -rf /var/lib/apt/lists/*
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 PYTHONUNBUFFERED=1 \
    BSK_REDIS_HOST=redis BSK_REDIS_PORT=6379 BSK_ACCOUNT_PORT=8080 BSK_WORLD_PORT=2051 \
    BSK_PUBLIC_ADDRESS=127.0.0.1
COPY docker/configure.py docker/world.sh /opt/bsk/docker/
ENTRYPOINT ["/usr/bin/tini", "--"]

# The account server. Its settings (Redis, port) are written into server.json at each start.
FROM dotnet-base AS account
COPY --from=dotnet-build /out/app /opt/bsk/app
RUN mkdir -p /opt/bsk/app/logs && chown -R app:app /opt/bsk/app
USER app
WORKDIR /opt/bsk/app
EXPOSE 8080
CMD ["sh", "-c", "python3 /opt/bsk/docker/configure.py account /opt/bsk/app/server.json && exec dotnet App.dll"]

# The world proxy and world servers, as bsk-world.service runs them: rolling_restart.py starts a
# world server from /opt/bsk/worldserver and turns into the proxy on BSK_WORLD_PORT. A rolling
# deploy inside a running container: `docker compose exec world python3 /opt/bsk/tools/rolling_restart.py
# request update` after copying a new build into /opt/bsk/worldserver.
FROM dotnet-base AS world
COPY --from=dotnet-build /out/worldserver /opt/bsk/worldserver
COPY tools/rolling_restart.py tools/world_proxy.py /opt/bsk/tools/
RUN mkdir -p /opt/bsk/build/live && chown -R app:app /opt/bsk
USER app
WORKDIR /opt/bsk
EXPOSE 2051
# docker stop (SIGTERM) has each world server save everyone, as systemctl stop does
CMD ["sh", "/opt/bsk/docker/world.sh"]

# --- the gateway and the web client ----------------------------------------------------------
FROM rust:1.96-bookworm AS rust-build
RUN apt-get update -q && apt-get install -y -q --no-install-recommends python3 \
 && rm -rf /var/lib/apt/lists/* \
 && rustup target add wasm32-unknown-unknown
# wasm-bindgen-cli at the version in Cargo.lock (tools/build_client_rs.py checks it; a Windows
# checkout has CRLF line endings)
COPY client-rs/Cargo.lock /tmp/Cargo.lock
RUN v=$(tr -d '\r' < /tmp/Cargo.lock | grep -A1 '^name = "wasm-bindgen"$' | sed -n 's/^version = "\(.*\)"$/\1/p') \
 && test -n "$v" && cargo install wasm-bindgen-cli --version "$v" --locked --root /usr/local
WORKDIR /src
COPY client-rs/ client-rs/
COPY client/src/ client/src/
COPY tools/build_client_rs.py tools/
RUN --mount=type=cache,target=/src/client-rs/target,sharing=locked \
    --mount=type=cache,target=/usr/local/cargo/registry,sharing=locked \
    python3 tools/build_client_rs.py --web \
 && cargo build --release -p bsk-gateway --manifest-path client-rs/Cargo.toml \
 && mkdir -p /out && cp client-rs/target/release/bsk-gateway /out/ && cp -r build/client-rs/web /out/web

# The gateway. Without --world-state it bridges to the world proxy (the world container), which
# is all a stack on one machine needs. The command line is the compose file's.
FROM debian:bookworm-slim AS gateway
RUN apt-get update -q && apt-get install -y -q --no-install-recommends ca-certificates \
 && rm -rf /var/lib/apt/lists/* \
 && useradd --system --home-dir /opt/bsk bsk
COPY --from=rust-build /out/bsk-gateway /opt/bsk/gateway/bsk-gateway
COPY --from=rust-build /out/web /opt/bsk/gateway/web
USER bsk
WORKDIR /opt/bsk/gateway
EXPOSE 8095
ENTRYPOINT ["/opt/bsk/gateway/bsk-gateway", "--web", "/opt/bsk/gateway/web"]

# --- the Discord bot -------------------------------------------------------------------------
# Its token comes from an env file at run time (docker-compose.yml's discord profile), never the
# image. It reads the changelog it posts from client-rs/CHANGELOG.md.
FROM python:3.12-slim-bookworm AS discord
COPY tools/discord/requirements.txt /opt/bsk/tools/discord/requirements.txt
RUN pip install --no-cache-dir --disable-pip-version-check -r /opt/bsk/tools/discord/requirements.txt \
 && useradd --system --home-dir /opt/bsk bsk && mkdir -p /opt/bsk/discord && chown bsk:bsk /opt/bsk/discord
COPY tools/discord/bot.py tools/discord/dodmg.py /opt/bsk/tools/discord/
COPY client-rs/CHANGELOG.md /opt/bsk/client-rs/CHANGELOG.md
ENV PYTHONUNBUFFERED=1 DISCORD_STATE=/opt/bsk/discord/state.json
USER bsk
WORKDIR /opt/bsk
ENTRYPOINT ["python3", "/opt/bsk/tools/discord/bot.py"]
