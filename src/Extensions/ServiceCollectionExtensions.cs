using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.FileSystem.Adapters;
using Snail.MCP.FileSystem.Application;
using Snail.MCP.FileSystem.Configuration;
using Snail.MCP.FileSystem.Tools;
using Snail.MCP.FileSystem.Tools.Prompts;

using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Discovery;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.FileSystem.Extensions;

/// <summary>The composition root: what the server is, in one place.</summary>
/// <remarks>
/// Two shapes, and they are not interchangeable. At an address every session names its own working copy,
/// so one server stands behind every worker of a machine and the root arrives in the URL. As a program
/// there is no URL to name anything: the server belongs to the one caller that started it and is confined
/// by the roots it was given. Everything between the two is the same server, which is why it is written once.
/// <para>
/// The session's address, its grant, its roots, <c>find_tool</c> and <c>run_tools</c> are Snail.Sdk's: this server wrote
/// them first, and the SDK took them from it for the whole fleet. Every tool of this server states an output schema, and
/// so do those two: they answer the whole envelope as their structured content, as the file tools do.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "A C# 14 extension block, which the guidelines ask for, compiles to a nested type; there is no nested type in the source.")]
public static class ServiceCollectionExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>Configures the server to answer sessions at an address, each naming the copy it works in.</summary>
        /// <remarks>
        /// The MCP SDK's default transport, which keeps no sessions: every request stands alone and is judged by the address
        /// it was posted to, so nothing carries over from one request to the next for a later one to rebind.
        /// </remarks>
        public IHostApplicationBuilder ConfigureMcpServerAtAnAddress(ServerConfig config)
        {
            builder.ConfigureMcpServer(config).WithHttpTransport().WithSessionAddress();

            return builder;
        }

        /// <summary>Configures the server to speak stdio to the process that started it.</summary>
        /// <remarks>
        /// Logging goes to standard error and nowhere else: standard output is the protocol here, and a
        /// single log line written to it is a corrupt message the client cannot parse. There is no address: every tool is
        /// granted, and the configured roots are the session's.
        /// </remarks>
        public IHostApplicationBuilder ConfigureMcpServerAsAProgram(ServerConfig config)
        {
            builder.Logging.UseStderr();
            builder.ConfigureMcpServer(config).WithStdioServerTransport();

            return builder;
        }

        /// <summary>What both shapes share: the configuration, the files over one disk, the session's roots on that disk, the tools.</summary>
        /// <remarks>
        /// One disk for the files and for the roots: <c>SessionRoots.Reach</c> follows a path on the disk the tools then read
        /// it from, so a share that stops answering is the session's <c>unreadable</c> at the reaching of a path as at the
        /// reading of the file, and a file in a folder the server may not enter is <c>unreadable</c>, never "there is no file".
        /// </remarks>
        private IMcpServerBuilder ConfigureMcpServer(ServerConfig config)
        {
            builder.AddServerConfiguration(config);

            var disk = new LocalDisk();

            builder.Services.AddSingleton<IWorkingCopy>(disk);
            builder.Services.AddSingleton<CopyReader>();
            builder.Services.AddSingleton<CopyWriter>();
            builder.Services.AddSingleton<CopyWalker>();
            builder.Services.AddSingleton<Files>();

            return builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = ServerIdentity.Of(typeof(ServiceCollectionExtensions).Assembly);
                    options.ServerInstructions = ServerGuidance.Instructions;
                })
                .ExplainFailures()
                .WithSessionGrant()
                .WithArgumentCheck()
                .WithSessionRoots(Volumes, options =>
                {
                    options.RootsSetting = ServerConfigExtensions.RootsSetting;
                    options.Disk = disk;
                })
                .WithTools<FileTools>(ToolJson.Options(FileSystemJson.Default))
                .WithToolDiscovery(options =>
                {
                    options.Meanings = new Dictionary<string, string>(ToolMeanings.All, StringComparer.Ordinal);
                    options.Shape = AnswerShape.Envelope;
                })
                .WithToolSequences(options =>
                {
                    options.MaxSteps = config.StepLimit;
                    options.OutputBudget = config.OutputLimitChars;
                    options.Shape = AnswerShape.Envelope;
                });
        }
    }

    /// <summary>The folders the server serves: the configured roots, a session's named root held inside them.</summary>
    internal static IReadOnlyList<string> Volumes(IServiceProvider services) => services.GetRequiredService<ServerConfig>().Roots;
}
