using Microsoft.AspNetCore.Http;

namespace Snail.MCP.FileSystem.Adapters;

/// <summary>The address of the request being served, which is all a session of this server is.</summary>
/// <remarks>
/// The transport keeps no sessions — stateless Streamable HTTP, the SDK's default since the 2026-07-28 revision — so
/// every request stands alone and is judged by the address it was posted to, inside the configured volume. Nothing
/// carries over from one request to the next for a later one to rebind; a guard test holds the transport to that, since
/// a transport with sessions would have to keep each session's address. The URL is written by the platform and never by
/// the model: a tool call cannot reach it, which is the whole reason the root travels here rather than in an argument.
/// </remarks>
internal sealed class HttpAddress(IHttpContextAccessor requests) : ISessionAddress
{
    public IReadOnlyList<string> Values(string parameter) =>
        requests.HttpContext is { } request
            ? [.. request.Request.Query[parameter].Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!)]
            : [];
}
