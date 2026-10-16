using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.FileSystem.Application;

namespace Snail.MCP.FileSystem.Extensions;

/// <summary>The grant of a session, checked where calls arrive rather than inside each tool.</summary>
[SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "A C# 14 extension block, which the guidelines ask for, compiles to a nested type; there is no nested type in the source.")]
public static class GrantRegistrationExtensions
{
    extension(IMcpServerBuilder builder)
    {
        /// <summary>Refuses a tool the session was not granted, whoever forwarded the call.</summary>
        /// <remarks>
        /// At the door, and not in <c>run_tools</c> and <c>run_script</c> one by one: a defence that
        /// lives in the tools that forward calls holds only until somebody adds a third one. Here every
        /// call passes the same check — the model's own, and the one a program sent a moment later.
        /// </remarks>
        public IMcpServerBuilder RefuseWhatWasNotGranted()
        {
            builder.WithRequestFilters(filters => filters.AddCallToolFilter(Refusing));

            return builder;
        }
    }

    /// <summary>The call handed on when the session was granted the tool, a refusal naming the grant otherwise.</summary>
    private static McpRequestHandler<CallToolRequestParams, CallToolResult> Refusing(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            SessionGrant? grant = context.Server.Services?.GetService<SessionGrant>();

            if (context.Params?.Name is not { } name || grant is null || grant.Allows(name))
            {
                return await next(context, cancellationToken);
            }

            var refusal = new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = grant.Refusal(name) }]
            };

            return refusal;
        };
}
