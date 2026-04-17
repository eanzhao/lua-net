using System.Diagnostics;
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

    [Fact]
    public void OfficialAttrib_ShouldRunSuccessfully()
    {
        var result = RunOfficialScript("attrib.lua");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("testing require");
        result.Output.ShouldContain("testing external strings");
        result.Output.ShouldContain("OK");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public void OfficialEvents_ShouldRunSuccessfully()
    {
        var result = RunOfficialScript("events.lua");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("testing metatables");
        result.Output.ShouldContain("OK");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public void OfficialNextVar_ShouldRunSuccessfully()
    {
        var result = RunOfficialScript("nextvar.lua");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("testing tables, next, and for");
        result.Output.ShouldContain("testing next x GC of deleted keys");
        result.Output.ShouldContain("testing floats in numeric for");
        result.Output.ShouldContain("OK");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public void OfficialSort_ShouldRunSuccessfully()
    {
        var result = RunOfficialScript("sort.lua");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("testing sort");
        result.Output.ShouldContain("sorting 50000 random elements");
        result.Output.ShouldContain("OK");
        result.Error.ShouldBeEmpty();
    }

    private static ScriptRunResult RunOfficialScript(string scriptPath)
    {
        return string.Equals(scriptPath, "attrib.lua", StringComparison.Ordinal) ||
               string.Equals(scriptPath, "gc.lua", StringComparison.Ordinal) ||
               string.Equals(scriptPath, "sort.lua", StringComparison.Ordinal)
            ? RunOfficialScriptViaCliProcess(scriptPath)
            : RunOfficialScriptInProcess(scriptPath);
    }

    private static ScriptRunResult RunOfficialScriptInProcess(string scriptPath)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var application = new LuaCliApplication(
            input: new StringReader(string.Empty),
            output: output,
            error: error);
        var suiteRoot = GetOfficialSuiteRoot();
        application.VirtualMachine.State.WorkingDirectory = suiteRoot;
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

    private static ScriptRunResult RunOfficialScriptViaCliProcess(string scriptPath)
    {
        var suiteRoot = GetOfficialSuiteRoot();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = suiteRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.StartInfo.ArgumentList.Add(typeof(LuaCliApplication).Assembly.Location);
        foreach (var argument in BuildCliArguments(scriptPath))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScriptRunResult(process.ExitCode, output, error);
    }

    private static IReadOnlyList<string> BuildCliArguments(string scriptPath)
    {
        if (!string.Equals(scriptPath, "attrib.lua", StringComparison.Ordinal))
        {
            return [scriptPath];
        }

        const string preloadChunk = """
package.preload["lib2-v2"] = function (...)
  return {
    id = function (...) return true end,
    newstr = function (s) return s end,
  }
end
""";

        return ["-e", preloadChunk, scriptPath];
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
