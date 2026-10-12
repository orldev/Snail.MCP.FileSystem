# The server as a container: one per machine, standing behind every agent on it.
#
#   docker build -t snail/s3l-filesystem-mcp .
#   docker run -p 8820:8820 -e SNAIL_MCP_FILESYSTEM_PORT=8820 -v workspaces:/workspaces snail/s3l-filesystem-mcp
#
# The volume is the point. This server answers about files, so it must run where the files are: the
# working copies of the machine are mounted here, and each session names the one it works in through
# its address — …/mcp?root=/workspaces/<task>. A session naming a copy this container cannot see is
# refused at the door rather than answered about the wrong folder.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/Snail.MCP.FileSystem.csproj -c Release -o /app -p:PublishAot=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

ENV SNAIL_MCP_FILESYSTEM_PORT=8820
EXPOSE 8820

ENTRYPOINT ["dotnet", "Snail.MCP.FileSystem.dll"]
