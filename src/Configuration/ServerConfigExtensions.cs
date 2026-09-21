using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Snail.MCP.FileSystem.Configuration;

/// <summary>Builds <see cref="ServerConfig"/>: defaults, then environment variables.</summary>
public static class ServerConfigExtensions
{
    /// <summary>What the environment says the server is, read before there is a host to read it into.</summary>
    /// <remarks>
    /// Read first and separately because the answer decides which host is built at all: a port means a
    /// web host listening for sessions, no port means a console speaking stdio to its parent. Binding it
    /// afterwards would be binding it too late.
    /// </remarks>
    public static ServerConfig Read()
    {
        var config = new ServerConfig();
        var configuration = Sources(new ConfigurationBuilder()).Build();

        configuration.GetSection(ServerConfig.SectionName).Bind(config);
        configuration.Bind(config);

        Normalize(config);
        Validate(config);

        return config;
    }

    /// <summary>Puts the configuration the host was started with where the rest of the server reads it.</summary>
    public static ServerConfig AddServerConfiguration(this IHostApplicationBuilder builder, ServerConfig config)
    {
        Sources(builder.Configuration);
        builder.Services.AddSingleton(config);

        return config;
    }

    private static IConfigurationBuilder Sources(IConfigurationBuilder configuration) =>
        configuration.AddEnvironmentVariables("SNAIL_MCP_FILESYSTEM_");

    /// <summary>Expands <c>~</c> and makes every root absolute, so a relative root cannot mean one folder here and another there.</summary>
    internal static void Normalize(ServerConfig config) =>
        config.Roots =
        [
            .. config.Roots
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Select(Expand)
                .Distinct(StringComparer.Ordinal)
        ];

    /// <summary>Refuses a limit outside its range at start-up, so a typo stops the process instead of being repaired in silence.</summary>
    internal static void Validate(ServerConfig config)
    {
        var faults = new List<string>();

        if (config.OutputLimitChars < 1024)
            faults.Add($"OutputLimitChars must be at least 1024, not {config.OutputLimitChars}");

        if (config.MaxFileBytes < 1024)
            faults.Add($"MaxFileBytes must be at least 1024, not {config.MaxFileBytes}");

        if (config.MaxMatches <= 0)
            faults.Add($"MaxMatches must be positive, not {config.MaxMatches}");

        if (config.StepLimit <= 0)
            faults.Add($"StepLimit must be positive, not {config.StepLimit}");

        if (faults.Count > 0)
            throw new InvalidOperationException($"invalid configuration: {string.Join("; ", faults)}");
    }

    internal static string Expand(string path)
    {
        var trimmed = path.Trim();

        if (trimmed == "~")
            return Home;

        if (trimmed.StartsWith("~/", StringComparison.Ordinal) || trimmed.StartsWith(@"~\", StringComparison.Ordinal))
            return Path.Combine(Home, trimmed[2..]);

        return Path.GetFullPath(trimmed);
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
