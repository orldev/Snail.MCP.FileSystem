using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snail.MCP.FileSystem.Tools;

/// <summary>The types of this server's tools the MCP SDK does not know, generated at build time for the native program.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ToolStep[]))]
[JsonSerializable(typeof(Answered<FileRead>))]
[JsonSerializable(typeof(Answered<FileWritten>))]
[JsonSerializable(typeof(Answered<FileEdited>))]
[JsonSerializable(typeof(Answered<FolderListed>))]
[JsonSerializable(typeof(Answered<FilesFound>))]
[JsonSerializable(typeof(Answered<TextFound>))]
[JsonSerializable(typeof(ToolsFound))]
[JsonSerializable(typeof(StepsRun))]
internal sealed partial class FileSystemJson : JsonSerializerContext;
