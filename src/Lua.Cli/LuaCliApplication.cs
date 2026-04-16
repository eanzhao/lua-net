using System.Text;
using Lua.Compiler;
using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Lua.Syntax.Lexing;
using Lua.VM;

namespace Lua.Cli;

public sealed class LuaCliApplication
{
    public const string VersionText = "lua-net (Lua 5.5)";

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public LuaCliApplication(
        LuaVirtualMachine? virtualMachine = null,
        TextReader? input = null,
        TextWriter? output = null,
        TextWriter? error = null)
    {
        VirtualMachine = virtualMachine ?? new LuaVirtualMachine();
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
        _error = error ?? Console.Error;

        VirtualMachine.State.PrintOutput = text => _output.WriteLine(text);
        VirtualMachine.State.WarningOutput = text => _error.WriteLine(text);
        SetArgTable(scriptPath: null, scriptArguments: []);
    }

    public LuaVirtualMachine VirtualMachine { get; }

    public int Run(IReadOnlyList<string> arguments)
    {
        LuaCliOptions options;
        try
        {
            options = LuaCliArgumentParser.Parse(arguments);
        }
        catch (ArgumentException ex)
        {
            _error.WriteLine(ex.Message);
            _error.WriteLine("Use '--help' to see available options.");
            return 1;
        }

        if (options.ShowHelp)
        {
            _output.WriteLine(GetUsageText());
            return 0;
        }

        if (options.ShowVersion)
        {
            _output.WriteLine(VersionText);
        }

        SetArgTable(options.ScriptPath, options.ScriptArguments);

        try
        {
            foreach (var chunk in options.ExecutedChunks)
            {
                ExecuteTextChunk(chunk, "=(command line)");
            }

            if (options.ScriptPath is not null)
            {
                ExecuteScript(options.ScriptPath, options.ScriptArguments);
            }

            if (options.EnterInteractive)
            {
                var repl = new LuaReplSession(this, _input, _output, _error);
                repl.Run(showBanner: !options.ShowVersion);
            }

            return 0;
        }
        catch (LuaSyntaxException ex)
        {
            _error.WriteLine(ex.Message);
            return 1;
        }
        catch (LuaCompilerException ex)
        {
            _error.WriteLine(ex.Message);
            return 1;
        }
        catch (LuaRuntimeException ex)
        {
            _error.WriteLine(FormatLuaError(ex.ErrorObject));
            return 1;
        }
    }

    public void ExecuteTextChunk(string source, string chunkName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chunkName);

        var chunk = LuaCompiler.Compile(source, chunkName);
        VirtualMachine.Execute(chunk);
    }

    public LuaValue[] EvaluateTextChunk(string source, string chunkName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chunkName);

        var chunk = LuaCompiler.Compile(source, chunkName);
        return VirtualMachine.Execute(chunk);
    }

    public void ExecuteScript(string scriptPath, IReadOnlyList<string> scriptArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        ArgumentNullException.ThrowIfNull(scriptArguments);

        var loadFile = GetGlobalFunction("loadfile");
        var loadResults = VirtualMachine.Call(
            loadFile,
            [
                LuaValue.FromString(scriptPath),
                LuaValue.FromString("bt")
            ]);

        if (loadResults.Length == 0 || loadResults[0].IsNil)
        {
            var errorObject = loadResults.Length > 1 ? loadResults[1] : LuaValue.FromString("failed to load script");
            throw new LuaRuntimeException(errorObject);
        }

        var scriptClosure = loadResults[0].AsFunction();
        var arguments = new LuaValue[scriptArguments.Count];
        for (var index = 0; index < scriptArguments.Count; index++)
        {
            arguments[index] = LuaValue.FromString(scriptArguments[index]);
        }

        VirtualMachine.Call(scriptClosure, arguments);
    }

    public string FormatResult(LuaValue value)
    {
        var tostring = GetGlobalFunction("tostring");
        var results = VirtualMachine.Call(tostring, [value]);
        return results.Length == 0 ? string.Empty : results[0].AsString();
    }

    public string FormatLuaError(LuaValue value)
    {
        return value.Kind == LuaValueKind.String
            ? value.AsString()
            : FormatResult(value);
    }

    public static string GetUsageText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Usage: lua-net [options] [script.lua [args...]]");
        builder.AppendLine();
        builder.AppendLine("Options:");
        builder.AppendLine("  -e chunk    execute a chunk of Lua code");
        builder.AppendLine("  -i          enter REPL after executing options or script");
        builder.AppendLine("  -v          print version information");
        builder.AppendLine("  -h, --help  show this help text");
        builder.AppendLine("  --          stop option parsing");
        return builder.ToString().TrimEnd();
    }

    private void SetArgTable(string? scriptPath, IReadOnlyList<string> scriptArguments)
    {
        var argTable = new LuaTable("arg", arrayCapacity: scriptArguments.Count);
        argTable.SetValue(
            LuaValue.FromInteger(0),
            LuaValue.FromString(string.IsNullOrWhiteSpace(scriptPath) ? "lua-net" : scriptPath));

        for (var index = 0; index < scriptArguments.Count; index++)
        {
            argTable.SetValue(LuaValue.FromInteger(index + 1), LuaValue.FromString(scriptArguments[index]));
        }

        VirtualMachine.State.GlobalEnvironment.SetValue(
            LuaValue.FromString("arg"),
            LuaValue.FromTable(argTable));
    }

    private LuaClosure GetGlobalFunction(string name)
    {
        var value = VirtualMachine.State.GlobalEnvironment.GetValue(LuaValue.FromString(name));
        return value.AsFunction();
    }
}
