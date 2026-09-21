namespace Snail.MCP.FileSystem.Tests.Support;

/// <summary>A working copy on disk, with the server pointed at it the way a session points it.</summary>
public sealed class Copy : IDisposable
{
    public Copy(Action<ServerConfig>? tune = null)
    {
        Root = Directory.CreateTempSubdirectory("snail-mcp-filesystem").FullName;
        Config = new ServerConfig { Roots = [Root] };
        tune?.Invoke(Config);
        ServerConfigExtensions.Normalize(Config);

        Roots = new SessionRoots(Config, new Nowhere());
        Files = new Files(Roots, Config);
    }

    public string Root { get; }

    public ServerConfig Config { get; }

    public SessionRoots Roots { get; }

    public Files Files { get; }

    /// <summary>Puts a file in the copy and answers with its full path.</summary>
    public string Write(string name, string text)
    {
        var path = Path.Combine(Root, name);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);

        return path;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    /// <summary>A session with no address: the configured roots stand, as for a server started as a program.</summary>
    private sealed class Nowhere : ISessionAddress
    {
        public IReadOnlyList<string> Values(string parameter) => [];
    }
}

/// <summary>A session opened at an address that names its own roots.</summary>
public sealed class Address(params string[] roots) : ISessionAddress
{
    public IReadOnlyList<string> Values(string parameter) =>
        parameter == SessionRoots.Parameter ? roots : [];
}
