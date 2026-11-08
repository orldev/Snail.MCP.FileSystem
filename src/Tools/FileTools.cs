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
    /// <summary>The text of a file, whole or by lines.</summary>
    [McpServerTool(Name = "read_file", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(Envelope<FileRead>))]
    [Description(ToolDescriptions.Read)]
    public CallToolResult Read(
        [Description(ToolDescriptions.Path)] string path,
        [Description(ToolDescriptions.ReadFrom)] int? from = null,
        [Description(ToolDescriptions.ReadTo)] int? to = null,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(files.Read(path, from, to, cancellationToken), AnswerShape.Envelope);

    /// <summary>A file written whole.</summary>
    [McpServerTool(Name = "write_file", Destructive = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(Envelope<FileWritten>))]
    [Description(ToolDescriptions.Write)]
    public CallToolResult Write(
        [Description(ToolDescriptions.Path)] string path,
        [Description(ToolDescriptions.WriteText)] string text) =>
        ToolAnswer.From(files.Write(path, text), AnswerShape.Envelope);

    /// <summary>An exact piece of a file replaced.</summary>
    [McpServerTool(Name = "edit_file", Destructive = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(Envelope<FileEdited>))]
    [Description(ToolDescriptions.Edit)]
    public CallToolResult Edit(
        [Description(ToolDescriptions.Path)] string path,
        [Description(ToolDescriptions.EditFind)] string find,
        [Description(ToolDescriptions.EditReplace)] string replace,
        [Description(ToolDescriptions.EditAll)] bool all = false) =>
        ToolAnswer.From(files.Edit(path, find, replace, all), AnswerShape.Envelope);

    /// <summary>What lies under a folder.</summary>
    [McpServerTool(Name = "list_directory", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(Envelope<FolderListed>))]
    [Description(ToolDescriptions.List)]
    public CallToolResult List(
        [Description(ToolDescriptions.ListPath)] string? path = null,
        [Description(ToolDescriptions.ListDepth)] int depth = 2,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(files.List(path, depth, cancellationToken), AnswerShape.Envelope);

    /// <summary>The files whose path matches a glob.</summary>
    [McpServerTool(Name = "find_files", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(Envelope<FilesFound>))]
    [Description(ToolDescriptions.Find)]
    public CallToolResult Find(
        [Description(ToolDescriptions.FindPattern)] string pattern,
        [Description(ToolDescriptions.ListPath)] string? path = null,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(files.Find(pattern, path, cancellationToken), AnswerShape.Envelope);

    /// <summary>The lines that contain a text.</summary>
    [McpServerTool(Name = "search_text", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(Envelope<TextFound>))]
    [Description(ToolDescriptions.Search)]
    public CallToolResult Search(
        [Description(ToolDescriptions.SearchQuery)] string query,
        [Description(ToolDescriptions.ListPath)] string? path = null,
        [Description(ToolDescriptions.SearchPattern)] string? pattern = null,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(files.Search(query, path, pattern, cancellationToken), AnswerShape.Envelope);
}
