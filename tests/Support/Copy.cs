using Snail.MCP.FileSystem.Adapters;

namespace Snail.MCP.FileSystem.Tests.Support;

/// <summary>A working copy on disk, with the server pointed at it the way a session points it.</summary>
public sealed class Copy : IDisposable
{
    /// <summary>A copy in a new temporary folder, read, written and reached through <paramref name="disk"/>, the machine's own by default.</summary>
    public Copy(Action<ServerConfig>? tune = null, IWorkingCopy? disk = null)
    {
        Root = TestFolder.Create();
        Config = new ServerConfig { Roots = [Root] };
        tune?.Invoke(Config);
        ServerConfigExtensions.Normalize(Config);

        Disk = disk ?? new LocalDisk();
        Roots = new SessionRoots(SessionAddress.None, Config.Roots, new SessionRootsOptions { Disk = Disk });
        Files = Files.Of(Roots, Config, Disk, Journal);
    }

    public string Root { get; }

    public ServerConfig Config { get; }

    public SessionRoots Roots { get; }

    public Files Files { get; }

    public IWorkingCopy Disk { get; }

    /// <summary>The journal of the changes this copy's files went through.</summary>
    public Journal<CopyWriter> Journal { get; } = new();

    /// <summary>The file tools of a session opened at an address, over the machine's own disk, its roots reached on the same disk.</summary>
    public static Files At(ServerConfig config, ISessionAddress address)
    {
        var disk = new LocalDisk();

        return Files.Of(new SessionRoots(address, config.Roots, new SessionRootsOptions { Disk = disk }), config, disk);
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
}

/// <summary>A session opened at an address that names its own roots.</summary>
public static class Address
{
    /// <summary>The address <c>?root=…&amp;root=…</c>; with no root, an address that names none.</summary>
    public static FixedAddress Of(params string[] roots) => new([.. roots.Select(root => (SessionRoots.Parameter, root))]);
}
