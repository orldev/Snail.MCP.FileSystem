namespace Snail.MCP.FileSystem.Tests.Support;

/// <summary>A test of what the program refuses outside a container, skipped — not passed — inside one.</summary>
/// <remarks>
/// Inside a container the same setting is allowed, so there is nothing to refuse: a test that returned early there was
/// counted as passed while it had checked nothing.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class OutsideAContainerFactAttribute : FactAttribute
{
    public const string Variable = "DOTNET_RUNNING_IN_CONTAINER";

    public OutsideAContainerFactAttribute()
    {
        if (string.Equals(Environment.GetEnvironmentVariable(Variable), "true", StringComparison.OrdinalIgnoreCase))
            Skip = $"checks a refusal outside a container, and {Variable}=true says this is one";
    }
}
