using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Snail.MCP.FileSystem.Tools;

/// <summary>The answer of a file tool that went through, as its output schema names it and as its text spells it.</summary>
/// <remarks>
/// A client that validates structured content, or turns a tool into a typed call, takes the shape from this schema. A
/// refusal is the same envelope with <c>ok</c> false, <c>data</c> null, an <c>error</c> and a <c>hint</c>, sent as an error
/// result in text alone, so it never has to fit the schema of what the tool answers when it works.
/// </remarks>
/// <typeparam name="T">What the tool answers.</typeparam>
[SuppressMessage("Snail", "S3L0008", Justification = "ok is the field every answer of this server and of the fleet carries, which clients already read.")]
public sealed record Answered<T>(
    [property: Description("true: the call did what was asked. A refusal comes as an error result with ok false, error and hint.")] bool Ok,
    T Data,
    [property: Description("Always null here: a refusal is an error result.")] string? Error = null,
    [property: Description("Always null here: a refusal is an error result.")] string? Hint = null);

/// <summary>What <c>read_file</c> answers.</summary>
public sealed record FileRead(
    [property: Description("The file, relative to the session's root.")] string Path,
    [property: Description("The first line given, counted from 1.")] int From,
    [property: Description("The last line given.")] int To,
    [property: Description("How many lines the file has.")] int Lines,
    [property: Description("The lines from..to, joined by line ends.")] string Text,
    [property: Description("Whether the text was cut at the server's output limit; read on from the line after the last one shown.")] bool IsCut);

/// <summary>What <c>write_file</c> answers.</summary>
public sealed record FileWritten(
    [property: Description("The file, relative to the session's root.")] string Path,
    [property: Description("Whether the file was not there before.")] bool IsCreated,
    [property: Description("The length of the file now, in bytes.")] long Bytes);

/// <summary>What <c>edit_file</c> answers.</summary>
public sealed record FileEdited(
    [property: Description("The file, relative to the session's root.")] string Path,
    [property: Description("How many places were replaced.")] int Replaced,
    [property: Description("The length of the file now, in bytes.")] long Bytes);

/// <summary>What <c>list_directory</c> answers.</summary>
public sealed record FolderListed(
    [property: Description("The folder listed, relative to the session's root.")] string Path,
    IReadOnlyList<FolderEntry> Entries,
    [property: Description("Whether there was more than the server lists at once.")] bool IsCut,
    [property: Description("Folders that could not be opened, so nothing in them is listed.")] IReadOnlyList<string> Unread,
    [property: Description("What the unread folders mean for the answer; null when there are none.")] string? Note);

/// <summary>One file or folder of a listing.</summary>
public sealed record FolderEntry(
    [property: Description("Relative to the session's root.")] string Path,
    [property: Description("Whether it is a folder.")] bool IsDirectory,
    [property: Description("The length of a file in bytes; 0 for a folder.")] long Bytes);

/// <summary>What <c>find_files</c> answers.</summary>
public sealed record FilesFound(
    [property: Description("The pattern as matched.")] string Pattern,
    [property: Description("The files matched, relative to the session's root.")] IReadOnlyList<string> Files,
    [property: Description("Whether there were more matches than the server gives at once.")] bool IsCut,
    [property: Description("Folders that could not be opened, so nothing in them was matched.")] IReadOnlyList<string> Unread,
    [property: Description("What the unread folders mean for the answer; null when there are none.")] string? Note);

/// <summary>What <c>search_text</c> answers.</summary>
public sealed record TextFound(
    [property: Description("The text searched for.")] string Query,
    IReadOnlyList<TextLine> Matches,
    [property: Description("Whether there were more matches than the server gives at once.")] bool IsCut,
    [property: Description("Files and folders not searched: too large, or not readable.")] IReadOnlyList<string> Unread,
    [property: Description("What the unread files mean for the answer; null when there are none.")] string? Note);

/// <summary>One line that holds the text searched for.</summary>
public sealed record TextLine(
    [property: Description("The file, relative to the session's root.")] string Path,
    [property: Description("The line's number, counted from 1.")] int Line,
    [property: Description("The line, cut to 240 characters.")] string Text);

/// <summary>What <c>find_tool</c> answers.</summary>
public sealed record ToolsFound(
    [property: Description("The task as asked.")] string? Task,
    [property: Description("Whether any tool matched; when none did, every tool is listed.")] bool IsMatched,
    [property: Description("Why every tool is listed; null when some matched.")] string? Note,
    IReadOnlyList<FoundTool> Tools);

/// <summary>One tool <c>find_tool</c> names, enough to call it.</summary>
public sealed record FoundTool(string Name, string Description, IReadOnlyList<FoundParameter> Parameters);

/// <summary>One parameter of a tool <c>find_tool</c> names.</summary>
public sealed record FoundParameter(
    string Name,
    [property: Description("The JSON type of the value.")] string Type,
    [property: Description("Whether the call must give it.")] bool IsRequired,
    string? Description);

/// <summary>What <c>run_tools</c> answers, whether every step ran or one stopped the run.</summary>
[SuppressMessage("Snail", "S3L0008", Justification = "ok is the field every answer of this server and of the fleet carries, which clients already read.")]
public sealed record StepsRun(
    [property: Description("Whether every step ran and went through.")] bool Ok,
    [property: Description("How many steps ran.")] int Ran,
    IReadOnlyList<StepRun> Steps,
    [property: Description("How many answers were left out to stay inside the output budget.")] int Omitted,
    [property: Description("Why answers were left out; null when none were.")] string? Note,
    [property: Description("Why the run stopped; null when every step went through.")] string? Error,
    [property: Description("What to do about it; null when there is nothing to do.")] string? Hint);

/// <summary>One step of a run: the tool's own answer, its refusal, or a note that its answer was left out.</summary>
[SuppressMessage("Snail", "S3L0008", Justification = "ok is the field every answer of this server and of the fleet carries, which clients already read.")]
public sealed record StepRun(
    [property: Description("The tool the step called.")] string Tool,
    [property: Description("Whether the step went through.")] bool Ok,
    [property: Description("The tool's answer as it gives it on its own call; absent when it was refused or left out.")] JsonNode? Data = null,
    [property: Description("The tool's refusal; absent when it went through.")] string? Error = null,
    [property: Description("Whether the answer was left out to stay inside the output budget; absent when it was not.")] bool IsOmitted = false);
