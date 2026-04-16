namespace Lua.Cli;

public sealed class LuaCliOptions
{
    public required bool ShowHelp { get; init; }

    public required bool ShowVersion { get; init; }

    public required bool EnterInteractive { get; init; }

    public required IReadOnlyList<string> ExecutedChunks { get; init; }

    public required string? ScriptPath { get; init; }

    public required IReadOnlyList<string> ScriptArguments { get; init; }

    public bool HasExecutionTarget => ExecutedChunks.Count > 0 || ScriptPath is not null;
}
