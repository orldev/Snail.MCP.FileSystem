# The server as a container: one per machine, standing behind every agent on it.
#
#   docker build -t snail/s3l-filesystem-mcp .
#   docker run -p 8820:8820 -e SNAIL_MCP_FILESYSTEM_PORT=8820 -v workspaces:/workspaces snail/s3l-filesystem-mcp
#
# The volume is the point. This server answers about files, so it must run where the files are: the
# working copies of the machine are mounted here, and each session names the one it works in through
# its address — …/mcp?root=/workspaces/<task>. A session naming a copy this container cannot see is
# refused at the door rather than answered about the wrong folder.
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

ENV SNAIL_MCP_FILESYSTEM_PORT=8820
EXPOSE 8820

ENTRYPOINT ["/app/Snail.MCP.FileSystem"]
