using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snail.MCP.FileSystem.Tools;

/// <summary>The types of this server's tools the MCP SDK does not know, generated at build time for the native program.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ToolStep[]))]
internal sealed partial class FileSystemJson : JsonSerializerContext;
