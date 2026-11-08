using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Snail.MCP.FileSystem.Configuration;

using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.FileSystem.Extensions;

/// <summary>The server as a running host: listening at an address when its settings name a port or HTTP, a child process otherwise.</summary>
public static class McpServerHost
{
    /// <summary>The path a session is opened at, and the one the platform writes into its registry.</summary>
    public const string Path = McpHttpHost.DefaultPath;

    /// <summary>Builds whichever of the two shapes the settings ask for.</summary>
    /// <remarks>
    /// Read first and separately because the answer decides which host is built at all: HTTP means a web host listening
    /// for sessions, stdio a console speaking to its parent. Binding it afterwards would be binding it too late. Either
    /// host is built over the settings read here rather than reading them again, so the log levels they name are its own.
    /// </remarks>
    public static IHost Of(string[] args)
    {
        IConfiguration settings = ServerSettings.Read(ServerConfigExtensions.Server);
        ServerConfig config = ServerConfigExtensions.Read(settings);
        McpEndpoint endpoint = ServerConfigExtensions.EndpointOf(settings);

        return endpoint.Transport is McpTransport.Http ? Listening(args, settings, config, endpoint) : Spoken(args, settings, config);
    }

    /// <summary>The server answering sessions at the endpoint; a port of zero takes any free one.</summary>
    /// <remarks>
    /// On loopback, unless the server runs inside a container, where the callers are the workers of the machine, each in its
    /// own container, and the published port is the boundary: the endpoint asks for no key, so the settings cannot open it
    /// beyond loopback anywhere else (<see cref="ServerConfigExtensions.EndpointOf"/>). The host
    /// is Snail.Sdk.Mcp.Http's in both shapes — its log lines on stderr, a request a line only on warnings, the idle trim —
    /// and a request that carries an <c>Origin</c> is refused: a page in a browser always sends one, an agent's client
    /// never does. A session naming a root this machine lacks, or one outside the configured roots, is turned away at the
    /// door before the protocol sees it. <c>/healthz</c> answers 200 with no body, for a container's healthcheck and for the
    /// test that waits for the program to be ready.
    /// </remarks>
    internal static WebApplication Listening(string[] args, IConfiguration settings, ServerConfig config, McpEndpoint endpoint)
    {
        WebApplicationBuilder builder = McpHttpHost.CreateBuilder(args, endpoint, settings);

        builder.ConfigureMcpServerAtAnAddress(config);

        WebApplication app = builder.Build();

        app.UseSessionRoots(ServiceCollectionExtensions.Volumes, Path);
        app.MapLocalMcp(Path);
        app.MapHealth();

        return app;
    }

    private static IHost Spoken(string[] args, IConfiguration settings, ServerConfig config)
    {
        HostApplicationBuilder builder = McpHost.CreateBuilder(args, settings);

        builder.ConfigureMcpServerAsAProgram(config);

        return builder.Build();
    }
}
