using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.FileSystem.Application;
using Snail.MCP.FileSystem.Configuration;

namespace Snail.MCP.FileSystem.Extensions;

/// <summary>What a session must satisfy before the protocol is allowed to see it.</summary>
public static class WebApplicationExtensions
{
    /// <summary>Turns away a session this server cannot serve, before a session exists to serve it.</summary>
    public static WebApplication RefuseASessionItCannotServe(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var named = context.Request.Query[SessionRoots.Parameter].Select(root => root ?? string.Empty).ToList();

            if (Refusal(app, named) is not { } refusal)
            {
                await next(context);
                return;
            }

            Log.SessionTurnedAway(app.Logger, refusal);

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/plain; charset=utf-8";

            await context.Response.WriteAsync(refusal);
        });

        return app;
    }

    /// <summary>Why this session may not be opened, or null when it may.</summary>
    /// <remarks>
    /// Two refusals. A session naming no root at a server configured with none would be served nothing
    /// anyway, and hearing that once at the door beats hearing it from every call for the life of the step.
    /// <para>
    /// The second is what pays for the whole arrangement: a session naming a root this machine does not
    /// have. That is the worst thing that can go wrong once servers answer at addresses — discovery hands
    /// the worker of one machine the server of another, and nothing in a call shows it. The server answers,
    /// about a folder that is not the task's copy, and a wrong answer wearing the shape of a right one is
    /// far worse than an error.
    /// </para>
    /// </remarks>
    private static string? Refusal(WebApplication app, IReadOnlyList<string> named)
    {
        var configured = app.Services.GetRequiredService<ServerConfig>().Roots;

        if (named.All(string.IsNullOrWhiteSpace) && configured.Length == 0)
            return "this session named no root and this server is configured with none, so it can serve nothing: "
                   + "open the session at /mcp?root=/path/to/the/copy";

        return SessionRoots.Absent(named) is { Count: > 0 } absent
            ? $"this machine has no {string.Join(", ", absent)}: the session named a working copy that is not on the host this server runs on"
            : null;
    }
}
