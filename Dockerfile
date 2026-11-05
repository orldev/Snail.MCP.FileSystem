# The server as a container: one per machine, standing behind every agent on it.
#
#   docker build --build-context feed=../../nuget -t snail/s3l-filesystem-mcp .
#   docker run -p 127.0.0.1:8820:8820 -v workspaces:/workspaces snail/s3l-filesystem-mcp
#
# The volume is the point. This server answers about files, so it must run where the files are: the
# working copies of the machine are mounted here, and each session names the one it works in through
# its address — …/mcp?root=/workspaces/<task>. A session naming a copy this container cannot see is
# refused at the door rather than answered about the wrong folder, and so is one naming a folder outside
# /workspaces, the configured root: a session cannot widen what the server was given to serve.
#
# The image runs as its app user (APP_UID, 1654), as every Snail image does. The server must write as whoever owns
# the copies: a volume made by Docker takes the owner of /workspaces below, but where something else writes the
# copies, the deployment runs the image as that writer. The Swarm stand's workers, git and roslyn write them as root,
# so the stand runs this image with user "0:0" — as app it would read a task's files and refuse to change them.
#
# The SDK is pinned to the band global.json asks for: on the floating sdk:10.0 a newer feature band would one day
# refuse the build. Snail.Sdk is taken from the author's local feed for now: the build takes it as a build context named
# feed, and restore finds it under the name nuget.config maps Snail.Sdk to.
#
# The server is native: the SDK compiles it ahead of time (Native AOT), which needs clang and zlib in the build stage,
# and the image runs the one executable on runtime-deps, with no .NET runtime in it — a smaller image and a faster
# start. It is compiled for the platform the image is built for (TARGETARCH: amd64 is linux-x64, arm64 is
# linux-arm64): ahead-of-time compilation does not cross architectures in the SDK image.
#
# VERSION is the release's when the shared mcp-image.yml builds the image (0.0.0-<branch>.<commit> from a branch)
# and 0.0.0-local otherwise: the server reports it in initialize and the image carries it as its OCI version.
ARG VERSION=0.0.0-local

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
ARG VERSION
ARG TARGETARCH
RUN apt-get update \
    && apt-get install --yes --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /source
COPY --from=feed . /feed
RUN dotnet nuget add source /feed --name LocalPackages
COPY . .
RUN case "$TARGETARCH" in amd64|"") rid=linux-x64 ;; *) rid="linux-$TARGETARCH" ;; esac \
    && dotnet publish src/Snail.MCP.FileSystem.csproj -c Release -r "$rid" -o /app -p:Version=$VERSION

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
ARG VERSION
LABEL org.opencontainers.image.version=$VERSION
# tini runs as process 1 and starts the program under it: .NET ends an unhandled exception with SIGABRT, which process
# 1 of a container ignores, so run as process 1 itself a program that fails at start spins at a full core and never
# exits, and nothing restarts it. Under tini it exits with 134.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends tini \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
RUN mkdir /workspaces && chown app:app /workspaces

# A container's published port does not reach its loopback, so the image opens the port on every interface; the endpoint
# asks for no credential, so the published port is the boundary: publish it where only the agents reach it.
ENV SNAIL_MCP_FILESYSTEM_PORT=8820
ENV SNAIL_MCP_FILESYSTEM_LISTENS_ON_EVERY_INTERFACE=true
ENV SNAIL_MCP_FILESYSTEM_ROOTS__0=/workspaces
EXPOSE 8820

USER app
ENTRYPOINT ["/usr/bin/tini", "--", "/app/Snail.MCP.FileSystem"]
