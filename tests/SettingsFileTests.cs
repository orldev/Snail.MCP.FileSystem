using System.Collections;
using System.Text.Json.Nodes;
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

    [Fact]
    public void Read_LimitThatMakesNoSense_StopsTheServer()
    {
        string file = SettingsFile(new JsonObject { ["StepLimit"] = 0 });

        Assert.Throws<InvalidOperationException>(() => ServerConfigExtensions.Read(Settings(file, new Hashtable())));
    }

    private static IConfiguration Settings(string? file, IDictionary variables) =>
        new ConfigurationBuilder().AddServerSettings(ServerConfigExtensions.Server, file, variables).Build();

    private string SettingsFile(JsonObject settings)
    {
        string file = Path.Combine(_scratch, "config.json");

        File.WriteAllText(file, settings.ToJsonString());

        return file;
    }
}
