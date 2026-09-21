using Microsoft.Extensions.Hosting;
using Snail.MCP.FileSystem.Extensions;

await McpServerHost.Of(args).RunAsync();
