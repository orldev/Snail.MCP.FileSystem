using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.FileSystem.Configuration;

/// <summary>Builds <see cref="ServerConfig"/>: defaults, then the settings file, then environment variables.</summary>
public static class ServerConfigExtensions
{
    /// <summary>The server's short name, which names its settings file and its variables.</summary>
    public const string Server = "filesystem";

    /// <summary>Where the server answers over HTTP when the settings name a transport or a port but no address: loopback, the image's port.</summary>
    public static readonly Uri DefaultUrl = new("http://127.0.0.1:8820");

    /// <summary>The setting that configures the roots, named in the hint to a session that has none.</summary>
    public const string RootsSetting = "SNAIL_MCP_FILESYSTEM_ROOTS__0";

    /// <summary>How the endpoint is read: keys under <c>Snail</c> too, and no farther than loopback outside a container.</summary>
    /// <remarks>The server's name spells the variable in a refusal under a test runner too, whose assembly names no server.</remarks>
    private static readonly McpEndpointOptions EndpointOptions = new()
    {
        FallbackSection = ServerConfig.SectionName,
        Reach = McpReach.LoopbackOrContainer,
        Server = Server
    };

    /// <summary>What every variable this server reads begins with.</summary>
    /// <remarks>The reading is Snail.Sdk.Mcp's, the same in every Snail server: <c>MAX_FILE_BYTES</c> is <c>MaxFileBytes</c>, <c>ROOTS__0</c> is <c>Roots:0</c>.</remarks>
    public const string Prefix = "SNAIL_MCP_FILESYSTEM_";

    /// <summary>What the given settings say, normalized and checked.</summary>
    internal static ServerConfig Read(IConfiguration configuration)
    {
        var config = new ServerConfig();

        ServerSettings.Bind(Server, configuration, settings =>
        {
            settings.GetSection(ServerConfig.SectionName).Bind(config);
            settings.Bind(config);
        });

        Normalize(config);
        Validate(config);

        return config;
    }

    /// <summary>Where the given settings say the server answers: stdio unless they name a transport or a port.</summary>
    /// <remarks>
    /// Snail.Sdk's reading, the same in every server of both transports: <c>PORT</c> alone means HTTP, <c>TRANSPORT</c>
    /// and <c>URL</c> say it the other way, at the root of the settings or under <c>Snail</c>, the root winning. The
    /// endpoint asks for no key, so it stays on loopback on this machine and opens every interface only inside a
    /// container, whose published port is the boundary; an address beyond loopback, or <c>LISTENS_ON_EVERY_INTERFACE</c>,
    /// outside one stops the server at its start, naming the variable.
    /// </remarks>
    public static McpEndpoint EndpointOf(IConfiguration configuration) => McpEndpoint.Read(configuration, DefaultUrl, EndpointOptions);

    /// <summary>Puts the configuration the host was started with where the rest of the server reads it.</summary>
    public static ServerConfig AddServerConfiguration(this IHostApplicationBuilder builder, ServerConfig config)
    {
        builder.Services.AddSingleton(config);

        return config;
    }

    /// <summary>Expands <c>~</c> and makes every root absolute, so a relative root cannot mean one folder here and another there.</summary>
    internal static void Normalize(ServerConfig config) =>
        config.Roots =
        [
            .. config.Roots
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Select(ServerFolders.FullPath)
                .Distinct(StringComparer.Ordinal)
        ];

    /// <summary>Refuses a limit outside its range at start-up, so a typo stops the process instead of being repaired in silence.</summary>
    /// <remarks>
    /// Each refused value is named as the operator set it, its variable and its key in the settings file
    /// (<see cref="ServerSettings.NameOfSetting"/>), all of them in one line: a <see cref="StartRefusedException"/> is what
    /// <c>Program.cs</c> writes to stderr before it exits with 1. <c>ListensOnEveryInterface</c> with no port to open is
    /// not checked here: <see cref="EndpointOf"/> refuses it over stdio, at the root of the settings or under <c>Snail</c>.
    /// </remarks>
    /// <exception cref="StartRefusedException">A setting holds a value the server cannot run on.</exception>
    internal static void Validate(ServerConfig config)
    {
        var faults = new List<string>();

        if (config.OutputLimitChars < 1024)
        {
            faults.Add(Fault("OutputLimitChars", config.OutputLimitChars, "a number of characters of at least 1024"));
        }

        if (config.MaxFileBytes < 1024)
        {
            faults.Add(Fault("MaxFileBytes", config.MaxFileBytes, "a number of bytes of at least 1024"));
        }

        if (config.MaxMatches <= 0)
        {
            faults.Add(Fault("MaxMatches", config.MaxMatches, "a number of matches above zero"));
        }

        if (config.StepLimit <= 0)
        {
            faults.Add(Fault("StepLimit", config.StepLimit, "a number of steps above zero"));
        }

        if (faults.Count > 0)
        {
            throw new StartRefusedException(string.Join(" ", faults));
        }
    }

    /// <summary>One refused value as a sentence: <c>SNAIL_MCP_FILESYSTEM_STEP_LIMIT (or StepLimit in the settings file) is '0'; it is a number of steps above zero.</c></summary>
    private static string Fault(string key, object value, string expected) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ServerSettings.NameOfSetting(Server, key)} is '{value}'; it is {expected}.");
}
