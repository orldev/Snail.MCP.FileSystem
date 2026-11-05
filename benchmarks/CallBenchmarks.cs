using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Snail.MCP.FileSystem.Adapters;
using Snail.MCP.FileSystem.Application;
using Snail.MCP.FileSystem.Application.Ports;
using Snail.MCP.FileSystem.Configuration;
using Snail.MCP.FileSystem.Domain;
using Snail.MCP.FileSystem.Tools;

namespace Snail.MCP.FileSystem.Benchmarks;

/// <summary>
/// What a tool call does most: find_tool scoring the catalog against a task, find_files matching a glob over a working
/// copy, search_text reading its lines. The copy is a generated tree in the temporary folder, made once and removed after.
/// </summary>
[MemoryDiagnoser]
public class CallBenchmarks
{
    private readonly ToolIndex _index = new();

    private string _root = string.Empty;

    private Files _files = null!;

    [GlobalSetup]
    public void Setup()
    {
        _root = Folder();

        for (int folder = 0; folder < 20; folder++)
        {
            string directory = Path.Combine(_root, $"area{folder}", "src");
            Directory.CreateDirectory(directory);

            for (int file = 0; file < 10; file++)
            {
                IEnumerable<string> lines = Enumerable.Range(0, 200).Select(line => line % 50 == 7 ? $"    var needle = {line}; // the line search_text looks for" : $"    var value{line} = Compute({line}, \"text of line {line}\");");
                File.WriteAllLines(Path.Combine(directory, file % 2 == 0 ? $"Type{file}.cs" : $"notes{file}.md"), lines);
            }
        }

        var config = new ServerConfig { Roots = [_root] };
        var disk = new LocalDisk();
        _files = Files.Of(new SessionRoots(config, new NoAddress(), disk), config, disk);
    }

    /// <summary>A new folder named as Snail.Sdk.Mcp.Testing names a test's, so a cleaner can prove a crashed run's is a leftover.</summary>
    /// <remarks>
    /// <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c>, the start in Unix seconds as the system reports it: the benchmarks build
    /// on Snail.Sdk, not on Snail.Sdk.Mcp, and do not reference the package, so they name the folder themselves, as the
    /// standard asks of a project without it. <see cref="Cleanup"/> deletes it when the run ends normally.
    /// </remarks>
    private static string Folder()
    {
        using var process = Process.GetCurrentProcess();
        long start = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();
        string folder = Path.Combine(Path.GetTempPath(), $"s3l-test-{Environment.ProcessId}-{start}-{Path.GetRandomFileName()[..8]}");

        Directory.CreateDirectory(folder);

        return folder;
    }

    [GlobalCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    [Benchmark]
    public int FindTool() => _index.Answer("replace one line in a file", 5).Count;

    [Benchmark]
    public Answer FindFiles() => _files.Find("**/*.cs", null);

    [Benchmark]
    public Answer SearchText() => _files.Search("needle", null, "*.cs");

    private sealed class NoAddress : ISessionAddress
    {
        public IReadOnlyList<string> Values(string parameter) => [];
    }
}
