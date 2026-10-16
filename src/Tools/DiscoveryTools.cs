using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.FileSystem.Tools.Prompts;

namespace Snail.MCP.FileSystem.Tools;

/// <summary>Finding the way in: which tool does the thing at hand.</summary>
[McpServerToolType]
public sealed class DiscoveryTools(ToolIndex index)
{
    /// <summary>The tools that answer a task described in words.</summary>
    [McpServerTool(Name = "find_tool", ReadOnly = true, OpenWorld = false)]
    [Description(ToolDescriptions.FindTool)]
    public CallToolResult FindTool(
        [Description(ToolDescriptions.FindToolTask)] string task,
        [Description(ToolDescriptions.FindToolLimit)] int limit = 5) =>
        ToolResponse.Json(index.Answer(task, limit));
}
