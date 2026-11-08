using Microsoft.Extensions.Hosting;
using Snail.MCP.FileSystem.Extensions;
using Snail.Sdk.Mcp.Hosting;

// A setting the operator got wrong is said in one line with exit code 1, not as a crash with a trace and 134.
try
{
    await McpServerHost.Of(args).RunAsync();

    return 0;
}
catch (StartRefusedException refused)
{
    await Console.Error.WriteLineAsync(refused.Message);

    return 1;
}
