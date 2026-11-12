using System.Diagnostics;

namespace Snail.MCP.FileSystem.Tests.Support;

/// <summary>The server started as a client starts it, with nothing on its input, and waited for until it exits.</summary>
/// <remarks>
/// A start refused at the settings exits before any client speaks. Started through <c>ServerProgram.StartAsync</c>, the
/// native program on a fast runner was gone before <c>initialize</c> was sent, and the MCP SDK threw "Failed to send
/// message" instead of the closed transport the refusal is read from. Here nothing is sent: the input is closed at once,
/// so a server that does not refuse ends at its end with 0 instead of hanging the test.
/// </remarks>
internal static class RefusedStart
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    /// <summary>The exit code and the standard error of the server started with these variables.</summary>
    public static async Task<(int ExitCode, string Stderr)> RunAsync(IDictionary<string, string?> environment)
    {
        (string command, string[] arguments) = ServerProgram.Command("Snail.MCP.FileSystem", "SNAIL_FILESYSTEM_SERVER");
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["HOME"] = ServerProgram.Home;
        start.Environment["USERPROFILE"] = ServerProgram.Home;
        start.Environment[$"{ServerConfigExtensions.Prefix}CONFIG"] = ServerProgram.NoSettingsFile;

        foreach ((string name, string? value) in environment)
        {
            start.Environment[name] = value;
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{command} did not start");
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        process.StandardInput.Close();

        using var limit = new CancellationTokenSource(Patience);

        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            throw new TimeoutException($"{command} did not exit within {Patience.TotalSeconds:0} s");
        }

        _ = await stdout;

        return (process.ExitCode, await stderr);
    }
}
