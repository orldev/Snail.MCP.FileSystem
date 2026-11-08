using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snail.MCP.FileSystem.Tools;

/// <summary>The types of this server's tools the MCP SDK does not know, generated at build time for the native program.</summary>
/// <remarks>
/// Each file tool states Snail.Sdk's <see cref="Envelope{T}"/> of what it answers as its output schema and answers with
/// <see cref="AnswerShape.Envelope"/>, so the structured content is the very envelope the text spells; a refusal is an
/// error result in text alone and never has to fit that schema. The native program serializes each closed type through
/// this context.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Envelope<FileRead>))]
[JsonSerializable(typeof(Envelope<FileWritten>))]
[JsonSerializable(typeof(Envelope<FileEdited>))]
[JsonSerializable(typeof(Envelope<FolderListed>))]
[JsonSerializable(typeof(Envelope<FilesFound>))]
[JsonSerializable(typeof(Envelope<TextFound>))]
internal sealed partial class FileSystemJson : JsonSerializerContext;
