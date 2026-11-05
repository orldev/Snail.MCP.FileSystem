using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Snail.MCP.FileSystem.Configuration;

using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.FileSystem.Extensions;

/// <summary>The server as a running host: listening at an address when it was given a port, a child process otherwise.</summary>
public static class McpServerHost
{
    /// <summary>The path a session is opened at, and the one the platform writes into its registry.</summary>
    public const string Path = "/mcp";

    /// <summary>Builds whichever of the two shapes the configuration asks for.</summary>
    public static IHost Of(string[] args)
    {
        ServerConfig config = ServerConfigExtensions.Read();

        return config.Port > 0 ? Listening(args, config) : Spoken(args, config);
    }

    /// <summary>The server answering sessions at <see cref="ServerConfig.Port"/>; a port of zero takes any free one.</summary>
    /// <remarks>
    /// On loopback, unless <see cref="ServerConfig.ListensOnEveryInterface"/> asks for every interface: in the image the callers are
    /// the workers of the machine, each in its own container, and the published port is the boundary. The host is
    /// Snail.Sdk.Mcp.Http's in both shapes — its log lines on stderr, a request a line only on warnings, the idle trim —
    /// and a request that carries an <c>Origin</c> is refused: a page in a browser always sends one, an agent's client
    /// never does.
    /// </remarks>
    internal static WebApplication Listening(string[] args, ServerConfig config)
    {
        WebApplicationBuilder builder = config.ListensOnEveryInterface
            ? McpHttpHost.CreateContainerBuilder(args, config.Port)
            : McpHttpHost.CreateBuilder(args, new Uri($"http://127.0.0.1:{config.Port}"));

        builder.Configuration.AddServerSettings(ServerConfigExtensions.Server);
        builder.ConfigureMcpServerAtAnAddress(config);

        WebApplication app = builder.Build();

        app.RefuseASessionItCannotServe();
        app.MapLocalMcp(Path);

        return app;
    }

    private static IHost Spoken(string[] args, ServerConfig config)
    {
        HostApplicationBuilder builder = McpHost.CreateBuilder(args);

        builder.ConfigureMcpServerAsAProgram(config);

        return builder.Build();
    }
}
