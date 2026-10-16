using Snail.MCP.FileSystem.Adapters;

namespace Snail.MCP.FileSystem.Tests.Support;

/// <summary>A working copy on disk, with the server pointed at it the way a session points it.</summary>
public sealed class Copy : IDisposable
{
    /// <summary>A copy in a new temporary folder, read and written through <paramref name="disk"/>, the machine's own by default.</summary>
    public Copy(Action<ServerConfig>? tune = null, IWorkingCopy? disk = null)
    {
        Root = TestFolder.Create();
        Config = new ServerConfig { Roots = [Root] };
        tune?.Invoke(Config);
        ServerConfigExtensions.Normalize(Config);

        Disk = disk ?? new LocalDisk();
        Roots = new SessionRoots(Config, new Nowhere(), Disk);
        Files = new Files(Roots, Config, Disk);
    }

    public string Root { get; }

    public ServerConfig Config { get; }

    public SessionRoots Roots { get; }

    public Files Files { get; }

    public IWorkingCopy Disk { get; }

    /// <summary>The file tools of a session opened at an address, over the machine's own disk.</summary>
    public static Files At(ServerConfig config, ISessionAddress address)
    {
        var disk = new LocalDisk();

        return new Files(new SessionRoots(config, address, disk), config, disk);
    }

    /// <summary>Puts a file in the copy and answers with its full path.</summary>
    public string Write(string name, string text)
    {
        string path = Path.Combine(Root, name);

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
