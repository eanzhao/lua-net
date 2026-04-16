namespace Lua.Cli;

public static class LuaCliArgumentParser
{
    public static LuaCliOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executedChunks = new List<string>();
        var showHelp = false;
        var showVersion = false;
        var interactive = arguments.Count == 0;
        string? scriptPath = null;
        IReadOnlyList<string> scriptArguments = Array.Empty<string>();

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (scriptPath is not null)
            {
                break;
            }

            if (argument == "--")
            {
                if (index + 1 < arguments.Count)
                {
                    scriptPath = arguments[index + 1];
                    scriptArguments = arguments.Skip(index + 2).ToArray();
                }

                break;
            }

            if (!argument.StartsWith("-", StringComparison.Ordinal) || argument == "-")
            {
                scriptPath = argument;
                scriptArguments = arguments.Skip(index + 1).ToArray();
                break;
            }

            switch (argument)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    interactive = false;
                    break;
                case "-v":
                    showVersion = true;
                    break;
                case "-i":
                    interactive = true;
                    break;
                case "-e":
                    if (index + 1 >= arguments.Count)
                    {
                        throw new ArgumentException("missing argument for '-e'");
                    }

                    executedChunks.Add(arguments[++index]);
                    break;
                default:
                    throw new ArgumentException($"unrecognized option '{argument}'");
            }
        }

        return new LuaCliOptions
        {
            ShowHelp = showHelp,
            ShowVersion = showVersion,
            EnterInteractive = interactive,
            ExecutedChunks = executedChunks,
            ScriptPath = scriptPath,
            ScriptArguments = scriptArguments
        };
    }
}
