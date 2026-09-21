using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.FileSystem.Application;
using Snail.MCP.FileSystem.Tools.Prompts;

namespace Snail.MCP.FileSystem.Tools;

/// <summary>The files of the working copy: reading them, writing them, and finding them.</summary>
[McpServerToolType]
public sealed class FileTools(Files files)
{
    [McpServerTool(Name = "read_file", ReadOnly = true, OpenWorld = false)]
    [Description(ToolDescriptions.Read)]
    public CallToolResult Read(
        [Description(ToolDescriptions.Path)] string path,
        [Description(ToolDescriptions.ReadFrom)] int? from = null,
        [Description(ToolDescriptions.ReadTo)] int? to = null) =>
        ToolResponse.From(files.Read(path, from, to));

    [McpServerTool(Name = "write_file", Destructive = true, OpenWorld = false)]
    [Description(ToolDescriptions.Write)]
    public CallToolResult Write(
        [Description(ToolDescriptions.Path)] string path,
        [Description(ToolDescriptions.WriteText)] string text) =>
        ToolResponse.From(files.Write(path, text));

    [McpServerTool(Name = "edit_file", Destructive = true, OpenWorld = false)]
    [Description(ToolDescriptions.Edit)]
    public CallToolResult Edit(
        [Description(ToolDescriptions.Path)] string path,
        [Description(ToolDescriptions.EditFind)] string find,
        [Description(ToolDescriptions.EditReplace)] string replace,
        [Description(ToolDescriptions.EditAll)] bool all = false) =>
        ToolResponse.From(files.Edit(path, find, replace, all));

    [McpServerTool(Name = "list_directory", ReadOnly = true, OpenWorld = false)]
    [Description(ToolDescriptions.List)]
    public CallToolResult List(
        [Description(ToolDescriptions.ListPath)] string? path = null,
        [Description(ToolDescriptions.ListDepth)] int depth = 2) =>
        ToolResponse.From(files.List(path, depth));

    [McpServerTool(Name = "find_files", ReadOnly = true, OpenWorld = false)]
    [Description(ToolDescriptions.Find)]
    public CallToolResult Find(
        [Description(ToolDescriptions.FindPattern)] string pattern,
        [Description(ToolDescriptions.ListPath)] string? path = null) =>
        ToolResponse.From(files.Find(pattern, path));

    [McpServerTool(Name = "search_text", ReadOnly = true, OpenWorld = false)]
    [Description(ToolDescriptions.Search)]
    public CallToolResult Search(
        [Description(ToolDescriptions.SearchQuery)] string query,
        [Description(ToolDescriptions.ListPath)] string? path = null,
        [Description(ToolDescriptions.SearchPattern)] string? pattern = null) =>
        ToolResponse.From(files.Search(query, path, pattern));
}
