using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Snail.MCP.FileSystem.Configuration;

using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.FileSystem.Extensions;

/// <summary>The server as a running host: listening at an address when it was given a port, a child process otherwise.</summary>
public static class McpServerHost
{
    /// <summary>The path a session is opened at, and the one the platform writes into its registry.</summary>
    public const string Path = "/mcp";

    /// <summary>Builds whichever of the two shapes the configuration asks for.</summary>
    public static IHost Of(string[] args)
    {
        var config = ServerConfigExtensions.Read();

        return config.Port > 0 ? Listening(args, config) : Spoken(args, config);
    }

    /// <summary>The server answering sessions at <see cref="ServerConfig.Port"/>; a port of zero takes any free one.</summary>
    /// <remarks>
    /// Bound on every interface because the callers are the workers of the machine, each in its own
    /// container: a server on loopback would be reachable from nothing but itself.
    /// </remarks>
    internal static WebApplication Listening(string[] args, ServerConfig config)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.Configuration.AddServerSettings(ServerConfigExtensions.Server);
        builder.ConfigureMcpServerAtAnAddress(config);

        var app = builder.Build();

        app.Urls.Add($"http://*:{config.Port}");
        app.RefuseASessionItCannotServe();
        app.MapMcp(Path);

        return app;
    }

    private static IHost Spoken(string[] args, ServerConfig config)
    {
        var builder = McpHost.CreateBuilder(args);

        builder.ConfigureMcpServerAsAProgram(config);

        return builder.Build();
    }
}
