# The server as a container: one per machine, standing behind every agent on it.
#
#   docker build -t snail/s3l-filesystem-mcp .
#   docker run -p 8820:8820 -e SNAIL_MCP_FILESYSTEM_PORT=8820 -v workspaces:/workspaces snail/s3l-filesystem-mcp
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
# refuse the build. The server is native: published for linux-x64 the SDK compiles it ahead of time (Native AOT), which
# needs clang and zlib in the build stage, and the image runs the one executable on runtime-deps, with no .NET runtime
# in it — a smaller image and a faster start.
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
RUN apt-get update \
    && apt-get install --yes --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /source
COPY . .
RUN dotnet publish src/Snail.MCP.FileSystem.csproj -c Release -r linux-x64 -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir /workspaces && chown app:app /workspaces

ENV SNAIL_MCP_FILESYSTEM_PORT=8820
ENV SNAIL_MCP_FILESYSTEM_ROOTS__0=/workspaces
EXPOSE 8820

USER app
ENTRYPOINT ["/app/Snail.MCP.FileSystem"]
