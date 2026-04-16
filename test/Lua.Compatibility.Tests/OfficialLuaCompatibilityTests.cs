using Lua.Cli;
using Shouldly;

namespace Lua.Compatibility.Tests;

public class OfficialLuaCompatibilityTests
{
    [Fact]
    public void OfficialBwCoercion_ShouldRunSuccessfully()
    {
        var result = RunOfficialScript("bwcoercion.lua");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldBeEmpty();
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public void OfficialBitwise_ShouldRunSuccessfully()
    {
        var result = RunOfficialScript("bitwise.lua");

        result.ExitCode.ShouldBe(0);
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public void OfficialLocals_ShouldRunSuccessfully()
    {
        var result = RunOfficialScript("locals.lua");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("testing errors in __close");
        result.Output.ShouldContain("to-be-closed variables in coroutines");
        result.Output.ShouldContain("OK");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public void OfficialGenGc_ShouldRunSuccessfully()
    {
        var result = RunOfficialScript("gengc.lua");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("testing generational garbage collection");
        result.Output.ShouldContain("OK");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public void OfficialGc_ShouldRunSuccessfully()
    {
        var result = RunOfficialScript("gc.lua");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("testing incremental garbage collection");
        result.Output.ShouldContain("weak tables");
        result.Output.ShouldContain("self-referenced threads");
        result.Output.ShouldContain("OK");
        result.Error.ShouldBeEmpty();
    }

    private static ScriptRunResult RunOfficialScript(string scriptPath)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new LuaCliApplication(
            input: new StringReader(string.Empty),
            output: output,
            error: error);
        var suiteRoot = GetOfficialSuiteRoot();

        application.VirtualMachine.State.FileReader = path =>
        {
            var fullPath = Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(suiteRoot, path));

            return File.ReadAllBytes(fullPath);
        };

        var exitCode = application.Run([scriptPath]);
        return new ScriptRunResult(exitCode, output.ToString(), error.ToString());
    }

    private static string GetOfficialSuiteRoot()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "test",
            "fixtures",
            "lua55",
            "official",
            "lua-5.5.0-tests"));
    }

    private readonly record struct ScriptRunResult(int ExitCode, string Output, string Error);
}
