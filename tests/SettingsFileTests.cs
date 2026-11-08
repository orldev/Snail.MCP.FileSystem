using System.Collections;
using Microsoft.Extensions.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>The settings file of Snail.Sdk's chain sets what the variables set, and a variable wins over the file.</summary>
public sealed class SettingsFileTests : IDisposable
{
    private readonly string _scratch = TestFolder.Create();

    public void Dispose() => Directory.Delete(_scratch, recursive: true);

    [Fact]
    public void Read_RootsInTheSettingsFile_AreTheRootsWithTheHomeExpanded()
    {
        string file = SettingsFile(new JsonObject { ["Roots"] = new JsonArray("~/src/app", _scratch), ["MaxMatches"] = 7 });

        ServerConfig config = ServerConfigExtensions.Read(Settings(file, new Hashtable()));

        Assert.Equal([Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "src/app"), _scratch], config.Roots);
        Assert.Equal(7, config.MaxMatches);
    }

    [Fact]
    public void Read_SettingsUnderTheSnailSection_AreRead()
    {
        string file = SettingsFile(new JsonObject { [ServerConfig.SectionName] = new JsonObject { ["MaxFileBytes"] = 4096 } });

        Assert.Equal(4096, ServerConfigExtensions.Read(Settings(file, new Hashtable())).MaxFileBytes);
    }

    [Fact]
    public void Read_VariableAndSettingsFile_VariableWins()
    {
        string file = SettingsFile(new JsonObject { ["AllowWrite"] = true, ["MaxMatches"] = 7 });
        var variables = new Hashtable { [$"{ServerConfigExtensions.Prefix}ALLOW_WRITE"] = "false" };

        ServerConfig config = ServerConfigExtensions.Read(Settings(file, variables));

        Assert.False(config.AllowWrite);
        Assert.Equal(7, config.MaxMatches);
    }

    /// <summary>
    /// The endpoint asks for no key, so outside a container no setting opens it beyond loopback: the flag that once did
    /// stops the start, naming the variable the operator set, in the program (<c>StdioContractTests</c>) and under the test
    /// runner alike. Inside a container the published port is the boundary and every interface is open without it.
    /// </summary>
    [Fact]
    public void Endpoint_EveryInterfaceOutsideAContainer_StopsTheServer()
    {
        var variables = new Hashtable
        {
            [$"{ServerConfigExtensions.Prefix}PORT"] = "8820",
            [$"{ServerConfigExtensions.Prefix}LISTENS_ON_EVERY_INTERFACE"] = "true"
        };

        if (IsInContainer)
        {
            Assert.True(ServerConfigExtensions.EndpointOf(Settings(null, variables)).ListensOnEveryInterface);

            return;
        }

        StartRefusedException refusal = Assert.Throws<StartRefusedException>(() => ServerConfigExtensions.EndpointOf(Settings(null, variables)));

        Assert.StartsWith($"{ServerConfigExtensions.Prefix}LISTENS_ON_EVERY_INTERFACE ", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>An address beyond loopback, every interface by its own name among them, is refused alike outside a container.</summary>
    [Theory]
    [InlineData("http://0.0.0.0:8820")]
    [InlineData("http://192.168.1.20:8820")]
    public void Endpoint_UrlBeyondLoopbackOutsideAContainer_StopsTheServer(string url)
    {
        var variables = new Hashtable
        {
            [$"{ServerConfigExtensions.Prefix}TRANSPORT"] = "Http",
            [$"{ServerConfigExtensions.Prefix}URL"] = url
        };

        if (IsInContainer)
        {
            return;
        }

        StartRefusedException refusal = Assert.Throws<StartRefusedException>(() => ServerConfigExtensions.EndpointOf(Settings(null, variables)));

        Assert.StartsWith($"{ServerConfigExtensions.Prefix}URL ", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"is '{url}'", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The endpoint's keys are read where the other settings are: under <c>Snail</c> as well as at the root.</summary>
    [Fact]
    public void Endpoint_PortUnderTheSnailSection_IsHttpAtThatPort()
    {
        string file = SettingsFile(new JsonObject { [ServerConfig.SectionName] = new JsonObject { ["Port"] = 8822 } });

        McpEndpoint endpoint = ServerConfigExtensions.EndpointOf(Settings(file, new Hashtable()));

        Assert.Equal(McpTransport.Http, endpoint.Transport);
        Assert.Equal(8822, endpoint.Url.Port);
    }

    /// <summary>No transport and no port: a program over stdio, whatever address a URL alone names.</summary>
    [Fact]
    public void Endpoint_NoPortNoTransport_IsStdio()
    {
        var variables = new Hashtable { [$"{ServerConfigExtensions.Prefix}URL"] = "http://127.0.0.1:9000" };

        Assert.Equal(McpTransport.Stdio, ServerConfigExtensions.EndpointOf(Settings(null, variables)).Transport);
    }

    /// <summary>A port alone is HTTP on loopback, outside a container: every interface is asked for, never assumed.</summary>
    [Fact]
    public void Endpoint_PortAlone_IsHttpOnLoopback()
    {
        var variables = new Hashtable { [$"{ServerConfigExtensions.Prefix}PORT"] = "8821" };

        McpEndpoint endpoint = ServerConfigExtensions.EndpointOf(Settings(null, variables));

        Assert.Equal(McpTransport.Http, endpoint.Transport);
        Assert.Equal(new Uri("http://127.0.0.1:8821/"), endpoint.Url);
        Assert.Equal(IsInContainer, endpoint.ListensOnEveryInterface);
    }

    /// <summary>Every interface with no port to open means nothing over stdio: Snail.Sdk's endpoint refuses it, naming the variable, inside a container too.</summary>
    [Fact]
    public void Endpoint_EveryInterfaceWithoutAPort_StopsTheServerNamingTheVariable()
    {
        var variables = new Hashtable { [$"{ServerConfigExtensions.Prefix}LISTENS_ON_EVERY_INTERFACE"] = "true" };

        StartRefusedException refusal = Assert.Throws<StartRefusedException>(() => ServerConfigExtensions.EndpointOf(Settings(null, variables)));

        Assert.StartsWith(
            $"{ServerConfigExtensions.Prefix}LISTENS_ON_EVERY_INTERFACE (or ListensOnEveryInterface in the settings file) is 'true'; ",
            refusal.Message,
            StringComparison.Ordinal);
    }

    /// <summary>The flag is heard under <c>Snail</c> as Snail.Sdk hears it there, so a stdio server refuses it there too.</summary>
    [Fact]
    public void Endpoint_EveryInterfaceUnderTheSnailSectionWithoutAPort_StopsTheServer()
    {
        string file = SettingsFile(new JsonObject { [ServerConfig.SectionName] = new JsonObject { ["ListensOnEveryInterface"] = true } });

        StartRefusedException refusal = Assert.Throws<StartRefusedException>(() => ServerConfigExtensions.EndpointOf(Settings(file, new Hashtable())));

        Assert.Contains("ListensOnEveryInterface", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_LimitThatMakesNoSense_StopsTheServerNamingTheVariable()
    {
        string file = SettingsFile(new JsonObject { ["StepLimit"] = 0 });

        StartRefusedException refusal = Assert.Throws<StartRefusedException>(() => ServerConfigExtensions.Read(Settings(file, new Hashtable())));

        Assert.Equal(
            $"{ServerConfigExtensions.Prefix}STEP_LIMIT (or StepLimit in the settings file) is '0'; it is a number of steps above zero.",
            refusal.Message);
    }

    private static bool IsInContainer =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);

    private static IConfiguration Settings(string? file, IDictionary variables) =>
        new ConfigurationBuilder().AddServerSettings(ServerConfigExtensions.Server, file, variables).Build();

    private string SettingsFile(JsonObject settings)
    {
        string file = Path.Combine(_scratch, "config.json");

        File.WriteAllText(file, settings.ToJsonString());

        return file;
    }
}
