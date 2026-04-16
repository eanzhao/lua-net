using System.Text;
using Lua.Runtime.Execution;
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
    public void Require_ShouldHandleTextAndNativeModulesThroughPackageSearchers()
    {
        const string source = """
package.path = "./mods/?.lua"
package.cpath = native_cpath

local text = require("text_mod")
local native, native_loader = require("native_mod")
local nested, nested_loader = require("root.nested")
local direct = assert(package.loadlib(native_path, "luaopen_native_mod"))("direct", native_path)
local found = assert(package.searchpath("text_mod", package.path))

return
    text.value,
    native.kind,
    native_loader,
    nested,
    nested_loader,
    direct.kind,
    direct.name,
    found
""";

        var vm = new LuaVirtualMachine();
        var extension = OperatingSystem.IsWindows() ? "dll" : OperatingSystem.IsMacOS() ? "dylib" : "so";
        var nativePath = $"./mods/native_mod.{extension}";
        var nativeCPath = $"./mods/?.{extension}";
        var rootPath = $"./mods/root.{extension}";

        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("native_path"), LuaValue.FromString(nativePath));
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("native_cpath"), LuaValue.FromString(nativeCPath));
        vm.State.FileReader = path => path switch
        {
            "./mods/text_mod.lua" => Encoding.Latin1.GetBytes("return { value = 41 }"),
            _ => throw new FileNotFoundException("missing")
        };
        vm.State.RegisterNativeLibraryFunction(
            nativePath,
            "luaopen_native_mod",
            (_, _, arguments) =>
            {
                var module = new LuaTable();
                module.SetValue(LuaValue.FromString("kind"), LuaValue.FromString("native"));
                module.SetValue(LuaValue.FromString("name"), arguments[0]);
                return [LuaValue.FromTable(module)];
            });
        vm.State.RegisterNativeLibraryFunction(
            rootPath,
            "luaopen_root_nested",
            (_, _, _) => [LuaValue.FromInteger(42)]);

        var results = vm.Execute(LuaCompiler.Compile(source, "package_step14.lua"));

        results.Length.ShouldBe(8);
        results[0].AsInteger().ShouldBe(41);
        results[1].AsString().ShouldBe("native");
        results[2].AsString().ShouldBe(nativePath);
        results[3].AsInteger().ShouldBe(42);
        results[4].AsString().ShouldBe(rootPath);
        results[5].AsString().ShouldBe("native");
        results[6].AsString().ShouldBe("direct");
        results[7].AsString().ShouldBe("./mods/text_mod.lua");
    }

    [Fact]
    public void Compile_ShouldSupportGlobalDeclarations()
    {
        const string source = """
local print = print
global none
global answer
answer = 42
return answer
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(42);
    }

    [Fact]
    public void Compile_ShouldRejectUndeclaredNameInsideExplicitGlobalScope()
    {
        var exception = Should.Throw<LuaCompilerException>(() => LuaCompiler.Compile("""
global none
return missing
"""));

        exception.Message.ShouldContain("variable 'missing' not declared");
    }

    [Fact]
    public void Compile_ShouldRejectAssignmentToConstGlobal()
    {
        var exception = Should.Throw<LuaCompilerException>(() => LuaCompiler.Compile("""
global<const> *
answer = 42
"""));

        exception.Message.ShouldContain("attempt to assign to const variable 'answer'");
    }

    [Fact]
    public void Compile_ShouldSupportGlobalFunctionDeclarations()
    {
        const string source = """
global none
global function fib(n)
    if n < 2 then
        return n
    end

    return fib(n - 1) + fib(n - 2)
end

return fib(6)
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(8);
    }

    [Fact]
    public void Compile_ShouldRaiseWhenGlobalInitializationFindsExistingValue()
    {
        var vm = new LuaVirtualMachine();
        vm.State.GlobalEnvironment.SetValue(LuaValue.FromString("answer"), LuaValue.FromInteger(1));

        var chunk = LuaCompiler.Compile("global answer = 42", "global_init.lua");
        var exception = Should.Throw<LuaRuntimeException>(() => vm.Execute(chunk));

        exception.ErrorObject.AsString().ShouldBe("global 'answer' already defined");
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
