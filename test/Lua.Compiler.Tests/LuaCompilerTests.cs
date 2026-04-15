using System.Text;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Lua.VM;
using Shouldly;

namespace Lua.Compiler.Tests;

public class LuaCompilerTests
{
    [Fact]
    public void Compile_ShouldExecuteArithmeticAndLocals()
    {
        const string source = """
local a = 40
local b = 2
return a + b, a * b
""";

        var results = Execute(source);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(42);
        results[1].AsInteger().ShouldBe(80);
    }

    [Fact]
    public void Compile_ShouldHandleIfWhileAndBreak()
    {
        const string source = """
local sum = 0
local i = 1

while i < 10 do
    if i == 5 then
        break
    end

    sum = sum + i
    i = i + 1
end

return sum, i
""";

        var results = Execute(source);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(10);
        results[1].AsInteger().ShouldBe(5);
    }

    [Fact]
    public void Compile_ShouldCaptureUpvaluesInNestedClosures()
    {
        const string source = """
local function outer(x)
    local y = 2

    return function(z)
        return x + y + z
    end
end

return outer(40)(0)
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
    }

    [Fact]
    public void Compile_ShouldHandleTablesAndMethodCalls()
    {
        const string source = """
local t = { answer = 41, [2] = "lua", 10 }

function t:add(delta)
    self.answer = self.answer + delta
    return self.answer
end

return t:add(1), t.answer, t[1], t[2]
""";

        var results = Execute(source);

        results.Length.ShouldBe(4);
        results[0].AsInteger().ShouldBe(42);
        results[1].AsInteger().ShouldBe(42);
        results[2].AsInteger().ShouldBe(10);
        results[3].AsString().ShouldBe("lua");
    }

    [Fact]
    public void LoadAndLoadFile_ShouldCompileTextChunks()
    {
        var vm = new LuaVirtualMachine();
        var load = GetBaseFunction(vm, "load");
        var loadfile = GetBaseFunction(vm, "loadfile");
        var environment = new LuaTable();
        environment.SetValue(LuaValue.FromString("x"), LuaValue.FromInteger(41));

        var loadResults = vm.Call(
            load,
            [
                LuaValue.FromString("return x + 1"),
                LuaValue.FromString("=(string)"),
                LuaValue.FromString("t"),
                LuaValue.FromTable(environment)
            ]);

        loadResults.Length.ShouldBe(1);
        loadResults[0].Kind.ShouldBe(LuaValueKind.Function);
        vm.Call(loadResults[0].AsFunction()).ShouldBe([LuaValue.FromInteger(42)]);

        vm.State.FileReader = _ => Encoding.Latin1.GetBytes("return x + 2");

        var loadFileResults = vm.Call(
            loadfile,
            [
                LuaValue.FromString("fixture.lua"),
                LuaValue.FromString("t"),
                LuaValue.FromTable(environment)
            ]);

        loadFileResults.Length.ShouldBe(1);
        vm.Call(loadFileResults[0].AsFunction()).ShouldBe([LuaValue.FromInteger(43)]);
    }

    [Fact]
    public void Compile_ShouldRejectUnsupportedGlobalDeclarations()
    {
        var exception = Should.Throw<LuaCompilerException>(() => LuaCompiler.Compile("global answer = 42"));

        exception.Message.ShouldContain("global declarations are not supported yet");
    }

    private static LuaValue[] Execute(string source)
    {
        var vm = new LuaVirtualMachine();
        var chunk = LuaCompiler.Compile(source, "sample.lua");
        return vm.Execute(chunk);
    }

    private static LuaClosure GetBaseFunction(LuaVirtualMachine vm, string name)
    {
        return vm.State.GlobalEnvironment.GetValue(LuaValue.FromString(name)).AsFunction();
    }
}
