using System.Text;
using Lua.Cli;
using Lua.Runtime.Values;
using Shouldly;

namespace Lua.Cli.Tests;

public class LuaCliTests
{
    [Fact]
    public void Parse_ShouldRecognizeScriptAndArguments()
    {
        var options = LuaCliArgumentParser.Parse(["script.lua", "first", "second"]);

        options.ScriptPath.ShouldBe("script.lua");
        options.ScriptArguments.ShouldBe(["first", "second"]);
        options.EnterInteractive.ShouldBeFalse();
        options.ShowVersion.ShouldBeFalse();
        options.ExecutedChunks.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_ShouldRecognizeChunksAndInteractiveMode()
    {
        var options = LuaCliArgumentParser.Parse(["-v", "-e", "answer = 41", "-i"]);

        options.ShowVersion.ShouldBeTrue();
        options.EnterInteractive.ShouldBeTrue();
        options.ExecutedChunks.ShouldBe(["answer = 41"]);
        options.ScriptPath.ShouldBeNull();
    }

    [Fact]
    public void Run_ShouldExecuteScriptAndPopulateArgTable()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new LuaCliApplication(
            input: new StringReader(string.Empty),
            output: output,
            error: error);
        application.VirtualMachine.State.FileReader = path => path switch
        {
            "script.lua" => Encoding.Latin1.GetBytes("""
print(arg[0], arg[1], arg[2])
captured = arg[1]
"""),
            _ => throw new FileNotFoundException(path)
        };

        var exitCode = application.Run(["script.lua", "first", "second"]);

        exitCode.ShouldBe(0);
        error.ToString().ShouldBeEmpty();
        output.ToString().ShouldContain("script.lua\tfirst\tsecond");
        application.VirtualMachine.State.GlobalEnvironment
            .GetValue(LuaValue.FromString("captured"))
            .AsString()
            .ShouldBe("first");
    }

    [Fact]
    public void Repl_ShouldEvaluateExpressionAndPrintResult()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new LuaCliApplication(
            input: new StringReader(string.Empty),
            output: output,
            error: error);
        var repl = new LuaReplSession(
            application,
            new StringReader("=1 + 1\n"),
            output,
            error);

        repl.Run(showBanner: false);

        error.ToString().ShouldBeEmpty();
        output.ToString().ShouldContain("2");
    }

    [Fact]
    public void Repl_ShouldAccumulateIncompleteInput()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new LuaCliApplication(
            input: new StringReader(string.Empty),
            output: output,
            error: error);
        var repl = new LuaReplSession(
            application,
            new StringReader("""
function answer()
return 42
end
=answer()
"""),
            output,
            error);

        repl.Run(showBanner: false);

        error.ToString().ShouldBeEmpty();
        output.ToString().ShouldContain("42");
    }
}
