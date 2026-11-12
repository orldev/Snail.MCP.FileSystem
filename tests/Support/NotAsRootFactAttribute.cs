namespace Snail.MCP.FileSystem.Tests.Support;

/// <summary>A test of what a process may not read, skipped — not failed — when the tests run as root.</summary>
/// <remarks>
/// Root enters a folder of mode 000 and reads what is in it, so there is nothing to refuse: the Linux runner of
/// BuildBridge runs its container as root, and three such tests failed there while they pass for every other user.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class NotAsRootFactAttribute : FactAttribute
{
    public NotAsRootFactAttribute()
    {
        if (!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess)
        {
            Skip = "checks a folder the process may not enter, and root may enter every folder";
        }
    }
}
