using Microsoft.AspNetCore.Http;

namespace Snail.MCP.FileSystem.Adapters;

/// <summary>The address the session was opened at, read from the request that is being served.</summary>
/// <remarks>
/// Every request of one session is posted to the same URL, so the query is on all of them and reading
/// the current one answers about the session. The URL is written by the platform and never by the model:
/// a tool call cannot reach it, which is the whole reason the root travels here rather than in an argument.
/// </remarks>
internal sealed class HttpAddress(IHttpContextAccessor requests) : ISessionAddress
{
    public IReadOnlyList<string> Values(string parameter) =>
        requests.HttpContext is { } request
            ? [.. request.Request.Query[parameter].Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!)]
            : [];
}
