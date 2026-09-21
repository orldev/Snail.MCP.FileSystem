using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.FileSystem.Adapters;
using Snail.MCP.FileSystem.Application;
using Snail.MCP.FileSystem.Configuration;
using Snail.MCP.FileSystem.Tools;
using Snail.MCP.FileSystem.Tools.Prompts;

namespace Snail.MCP.FileSystem.Extensions;

/// <summary>The composition root: what the server is, in one place.</summary>
/// <remarks>
/// Two shapes, and they are not interchangeable. At an address every session names its own working copy,
/// so one server stands behind every worker of a machine and the root arrives in the URL. As a program
/// there is no URL to name anything: the server belongs to the one caller that started it and is confined
/// by the roots it was given. Everything between the two is the same server, which is why it is written once.
/// </remarks>
public static class ServiceCollectionExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>Configures the server to answer sessions at an address, each naming the copy it works in.</summary>
        public IHostApplicationBuilder ConfigureMcpServerAtAnAddress(ServerConfig config)
        {
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<ISessionAddress, HttpAddress>();
            builder.ConfigureMcpServer(config).WithHttpTransport();

            return builder;
        }

        /// <summary>Configures the server to speak stdio to the process that started it.</summary>
        /// <remarks>
        /// Logging goes to standard error and nowhere else: standard output is the protocol here, and a
        /// single log line written to it is a corrupt message the client cannot parse.
        /// </remarks>
        public IHostApplicationBuilder ConfigureMcpServerAsAProgram(ServerConfig config)
        {
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddSingleton<ISessionAddress, StdioAddress>();
            builder.ConfigureMcpServer(config).WithStdioServerTransport();

            return builder;
        }

        private IMcpServerBuilder ConfigureMcpServer(ServerConfig config)
        {
            builder.AddServerConfiguration(config);

            builder.Services.AddSingleton<SessionRoots>();
            builder.Services.AddSingleton<SessionGrant>();
            builder.Services.AddSingleton<Files>();
            builder.Services.AddSingleton<ToolIndex>();

            return builder.Services
                .AddMcpServer(options => options.ServerInstructions = ServerGuidance.Instructions)
                .RefuseWhatWasNotGranted()
                .WithTools<FileTools>()
                .WithTools<DiscoveryTools>()
                .WithTools<SequenceTools>();
        }
    }
}
