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
    public void Compile_ShouldSupportUnnamedVarargFunctions()
    {
        const string source = """
local function spread(...)
    local a, b, c = ...
    local t = {...}
    return a, b, c, t[1], t[2], t[3], t[4]
end

return spread(10, 20, 30)
""";

        var results = Execute(source);

        results.Length.ShouldBe(7);
        results[0].AsInteger().ShouldBe(10);
        results[1].AsInteger().ShouldBe(20);
        results[2].AsInteger().ShouldBe(30);
        results[3].AsInteger().ShouldBe(10);
        results[4].AsInteger().ShouldBe(20);
        results[5].AsInteger().ShouldBe(30);
        results[6].IsNil.ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldForwardVarargIntoLastCallArgument()
    {
        const string source = """
local function pack(...)
    return {...}
end

local function forward(...)
    return pack("head", ...)
end

local t = forward(10, 20, 30)
return t[1], t[2], t[3], t[4], t[5]
""";

        var results = Execute(source);

        results.Length.ShouldBe(5);
        results[0].AsString().ShouldBe("head");
        results[1].AsInteger().ShouldBe(10);
        results[2].AsInteger().ShouldBe(20);
        results[3].AsInteger().ShouldBe(30);
        results[4].IsNil.ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldSupportNamedVarargTableParameters()
    {
        const string source = """
local function f(x, ...args)
    local first, second = ...
    args[2] = args[2] + x
    return first, second, args[1], args[2], args[3], args.n
end

return f(5, 10, 20, 30)
""";

        var results = Execute(source);

        results.Length.ShouldBe(6);
        results[0].AsInteger().ShouldBe(10);
        results[1].AsInteger().ShouldBe(20);
        results[2].AsInteger().ShouldBe(10);
        results[3].AsInteger().ShouldBe(25);
        results[4].AsInteger().ShouldBe(30);
        results[5].AsInteger().ShouldBe(3);
    }

    [Fact]
    public void Compile_ShouldTreatMainChunkAsVararg()
    {
        const string source = """
local t = {...}
return select("#", ...), t[1], t[2], t[3]
""";

        var vm = new LuaVirtualMachine();
        var chunk = LuaCompiler.Compile(source, "sample.lua");
        var closure = vm.CreateClosure(chunk.MainFunction);
        var results = vm.Call(
            closure,
            [LuaValue.FromString("names"), LuaValue.FromString("libs/names.lua")]);

        results.Length.ShouldBe(4);
        results[0].AsInteger().ShouldBe(2);
        results[1].AsString().ShouldBe("names");
        results[2].AsString().ShouldBe("libs/names.lua");
        results[3].IsNil.ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldUseArithmeticRuntimeErrorTextForMissingOperands()
    {
        const string source = """
local st, msg = pcall(function ()
    local a = nil
    return a + 1
end)

return tostring(st), string.find(msg, "arithmetic") ~= nil
""";

        var results = Execute(source);

        results.Length.ShouldBe(2);
        results[0].AsString().ShouldBe("false");
        results[1].AsBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldKeepLargeRoundedFloatsDistinctFromNeighborIntegers()
    {
        const string source = """
local a = {}
local maxint = math.maxinteger
while maxint ~= (maxint + 0.0) or (maxint - 1) ~= (maxint - 1.0) do
  maxint = maxint // 2
end

local maxintF = maxint + 0.0
a[maxintF] = 10
a[maxintF - 1.0] = 11
a[-maxintF] = 12
a[-maxintF + 1.0] = 13

return maxint, a[maxint], a[maxint - 1], a[-maxint], a[-maxint + 1]
""";

        var results = Execute(source);

        results.Length.ShouldBe(5);
        results[0].AsInteger().ShouldBeLessThan(long.MaxValue);
        results[1].AsInteger().ShouldBe(10);
        results[2].AsInteger().ShouldBe(11);
        results[3].AsInteger().ShouldBe(12);
        results[4].AsInteger().ShouldBe(13);
    }

    [Fact]
    public void Compile_ShouldTreatNegativeTwoToSixtyThreeAsIntegerMinimum()
    {
        const string source = """
local shifted = load("return -1 >> -9223372036854775808")()
return shifted, math.type(-9223372036854775808)
""";

        var results = Execute(source);

        results.Length.ShouldBe(2);
        results[0].AsInteger().ShouldBe(0);
        results[1].AsString().ShouldBe("integer");
    }

    [Fact]
    public void Compile_ShouldTreatExactlyRepresentableFloatPowersAsEqualToIntegers()
    {
        const string source = """
local a = -3
a = a + 1125899906842627
local t = {}
t[2^50] = "match"

return a == 2^50, a <= 2^50, a >= 2^50, t[1125899906842624]
""";

        var results = Execute(source);

        results.Length.ShouldBe(4);
        results[0].AsBoolean().ShouldBeTrue();
        results[1].AsBoolean().ShouldBeTrue();
        results[2].AsBoolean().ShouldBeTrue();
        results[3].AsString().ShouldBe("match");
    }

    [Fact]
    public void Compile_ShouldSupportNumericForWithDefaultStep()
    {
        const string source = """
local sum = 0

for i = 1, 5 do
    sum = sum + i
end

return sum
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(15);
    }

    [Fact]
    public void Compile_ShouldSupportNumericForWithBreak()
    {
        const string source = """
local sum = 0

for i = 1, 10, 2 do
    if i == 5 then
        break
    end

    sum = sum + i
end

return sum
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(4);
    }

    [Fact]
    public void Compile_ShouldSupportFloatFor()
    {
        const string source = """
local sum = 0.0

for x = 1.5, 4.5, 1.5 do
    sum = sum + x
end

return sum
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsFloat().ShouldBe(9d, 1e-12);
    }

    [Fact]
    public void Compile_ShouldSupportGenericForWithExplicitIterator()
    {
        const string source = """
local function iter(state, control)
    local next = control + 1
    if next <= state then
        return next, next * 10
    end

    return nil
end

local sum = 0

for i, v in iter, 3, 0 do
    sum = sum + i + v
end

return sum
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(66);
    }

    [Fact]
    public void Compile_ShouldSupportGenericForWithCallMultiResults()
    {
        const string source = """
local sum = 0

for _, value in pairs({10, 20, 30}) do
    sum = sum + value
end

return sum
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(60);
    }

    [Fact]
    public void Compile_ShouldUseLogicalRightShiftSemantics()
    {
        const string source = """
local a = 0xF0F0F0F0F0F0F0F0
return a >> 4, ~a, 0x12345678 >> -8, 0x12345678 << 8
""";

        var results = Execute(source);

        results.Length.ShouldBe(4);
        results[0].AsInteger().ShouldBe(results[1].AsInteger());
        results[2].AsInteger().ShouldBe(results[3].AsInteger());
    }

    [Fact]
    public void Compile_ShouldFallbackToStringBitwiseMetamethods()
    {
        const string source = """
local smt = getmetatable("")
smt.__band = function (x, y)
    return tonumber(x) & tonumber(y)
end

return "0xAA.0" & "0xF0.0"
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(0xA0);
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
    public void Compile_ShouldRejectAssignmentToConstLocal()
    {
        var exception = Should.Throw<LuaCompilerException>(() => LuaCompiler.Compile("""
local value<const> = 42
value = 99
"""));

        exception.Message.ShouldContain("attempt to assign to const variable 'value'");
    }

    [Fact]
    public void Compile_ShouldRejectAssignmentToCapturedConstLocal()
    {
        var exception = Should.Throw<LuaCompilerException>(() => LuaCompiler.Compile("""
local answer<const> = 42

local function mutate()
    answer = 99
end
"""));

        exception.Message.ShouldContain("attempt to assign to const variable 'answer'");
    }

    [Fact]
    public void Compile_ShouldTreatForControlVariablesAsReadOnly()
    {
        var exception = Should.Throw<LuaCompilerException>(() => LuaCompiler.Compile("""
for i = 1, 3 do
    i = i + 1
end
"""));

        exception.Message.ShouldContain("attempt to assign to const variable 'i'");
    }

    [Fact]
    public void Compile_ShouldAllowAssigningNonControlVariablesInGenericFor()
    {
        Should.NotThrow(() => LuaCompiler.Compile("""
for _, value in pairs({ 1, 2, 3 }) do
    value = value + 1
end
"""));
    }

    [Fact]
    public void Compile_ShouldReportActiveLineForSingleLineEmptyFunction()
    {
        var results = Execute("""
local info = debug.getinfo(function () end, "SL")
return info.activelines[info.linedefined] == true
""");

        results.ShouldHaveSingleItem();
        results[0].AsBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldNotReportDefinitionLineForMultiLineFunction()
    {
        var results = Execute("""
local function sample()
    local value = 1
    return value
end

local info = debug.getinfo(sample, "SL")
return info.activelines[info.linedefined] == nil,
       info.activelines[info.linedefined + 1] == true,
       info.activelines[info.linedefined + 2] == true
""");

        results.Length.ShouldBe(3);
        results[0].AsBoolean().ShouldBeTrue();
        results[1].AsBoolean().ShouldBeTrue();
        results[2].AsBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldNotReportNameForFunctionObjectInDebugGetInfo()
    {
        var results = Execute("""
local function sample()
    return 1
end

local info = debug.getinfo(sample, "n")
return info.name == nil, info.namewhat == ""
""");

        results.Length.ShouldBe(2);
        results[0].AsBoolean().ShouldBeTrue();
        results[1].AsBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldNotLeakPairsHelperLinesIntoDebugHooks()
    {
        var results = Execute("""
local trace = {}

local function test (s)
  collectgarbage()
  local function hook (event, line)
    assert(event == 'line')
    trace[#trace + 1] = line
  end

  debug.sethook(hook, 'l'); load(s)(); debug.sethook()
end

test([[for i,v in pairs{'a','b'} do
  a=tostring(i) .. v
end
]])

return table.concat(trace, ",")
""");

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("1,2,1,2,1,3");
    }

    [Fact]
    public void Compile_ShouldCloseToBeClosedLocalsAtScopeExit()
    {
        const string source = """
local log = ""
local mt = {
    __close = function(self)
        log = log .. self.tag
    end
}

do
    local a <close> = setmetatable({ tag = "a" }, mt)
    do
        local b <close> = setmetatable({ tag = "b" }, mt)
    end

    log = log .. "x"
end

return log
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("bxa");
    }

    [Fact]
    public void Compile_ShouldCloseToBeClosedLocalsWhenBreakingOutOfLoop()
    {
        const string source = """
local log = ""
local mt = {
    __close = function(self)
        log = log .. self.tag
    end
}

while true do
    local x <close> = setmetatable({ tag = "x" }, mt)
    break
end

return log
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("x");
    }

    [Fact]
    public void Compile_ShouldAllowRepeatConditionToSeeBlockLocals()
    {
        const string source = """
local total = 0

repeat
    local nextValue = total + 1
    total = nextValue
until nextValue == 2

return total
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsInteger().ShouldBe(2);
    }

    [Fact]
    public void Compile_ShouldCloseToBeClosedLocalsWhenRepeatContinues()
    {
        const string source = """
local log = ""
local mt = {
    __close = function(self)
        log = log .. self.tag
    end
}

local i = 0
repeat
    i = i + 1
    local tag = i == 1 and "a" or "b"
    local x <close> = setmetatable({ tag = tag }, mt)
until i == 2

return log
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsString().ShouldBe("ab");
    }

    [Fact]
    public void Compile_ShouldPassCloseErrorArgumentOnlyOnExceptionalUnwind()
    {
        const string source = """
local trace = {}

local function func2close(f, x, y)
    local obj = setmetatable({}, { __close = f })
    if x then
        return x, obj, y
    end

    return obj
end

local function foo(howtoclose, obj, n)
    do
        local a <close> = func2close(function(...t)
            trace[#trace + 1] = string.format("%s:%d:%s:%s", howtoclose, select("#", ...), tostring(t.n), tostring(t[2]))
        end)

        if howtoclose == "ret" then
            return obj
        end

        if howtoclose == "err" then
            error(obj)
        end
    end
end

foo("scope", nil, 1)
local ret = foo("ret", 32, 1)
local st, msg = pcall(foo, "err", 23, 2)

return trace[1], ret, trace[2], tostring(st) .. ":" .. tostring(msg), trace[3]
""";

        var results = Execute(source);

        results.Length.ShouldBe(5);
        results[0].AsString().ShouldBe("scope:1:1:nil");
        results[1].AsInteger().ShouldBe(32);
        results[2].AsString().ShouldBe("ret:1:1:nil");
        results[3].AsString().ShouldBe("false:23");
        results[4].AsString().ShouldBe("err:2:2:23");
    }

    [Fact]
    public void Compile_ShouldCloseGenericForFourthValueAtLoopExit()
    {
        const string source = """
local flag = false
local closeValue = setmetatable({}, {
    __close = function()
        flag = true
    end
})

local function values()
    return (function() return nil end), nil, nil, closeValue
end

for k in values() do
end

return flag
""";

        var results = Execute(source);

        results.ShouldHaveSingleItem();
        results[0].AsBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldExposeCallerOutsideUnwoundFrameDuringClose()
    {
        const string source = """
local debug = require("debug")
local normal
local unwound

local function func2close(f)
    return setmetatable({}, { __close = f })
end

local function ok()
    local _ <close> = func2close(function()
        normal = debug.getinfo(2).name
    end)

    return 1
end

local function bad()
    local _ <close> = func2close(function(_, msg)
        unwound = debug.getinfo(2).name .. ":" .. tostring(msg)
    end)

    error(4)
end

ok()
local st, msg = pcall(bad)

return normal, unwound, tostring(st) .. ":" .. tostring(msg)
""";

        var results = Execute(source);

        results.Length.ShouldBe(3);
        results[0].AsString().ShouldBe("ok");
        results[1].AsString().ShouldBe("pcall:4");
        results[2].AsString().ShouldBe("false:4");
    }

    [Fact]
    public void Compile_ShouldPrefixStringErrorsAndSkipTracebackFrameByDefault()
    {
        const string source = """
local debug = require("debug")

local function func2close(f)
    return setmetatable({}, { __close = f })
end

local function foo()
    do
        local x1 <close> = func2close(function(self, msg)
            error("@Y")
        end)

        local x123 <close> = func2close(function(_, msg)
            error("@X")
        end)
    end
end

local st, msg = xpcall(foo, debug.traceback)

return tostring(st), string.match(msg, "^[^ ]* @Y") ~= nil, string.find(msg, "'debug.traceback'") == nil
""";

        var results = Execute(source);

        results.Length.ShouldBe(3);
        results[0].AsString().ShouldBe("false");
        results[1].AsBoolean().ShouldBeTrue();
        results[2].AsBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldReportNonClosableValuesWithLuaCompatibleMessages()
    {
        const string source = """
local function first()
    local x <close> = {}
end

local function second()
    local xyz <close> = setmetatable({}, { __close = print })
    getmetatable(xyz).__close = nil
end

local function func2close(f, x, y)
    local obj = setmetatable({}, { __close = f })
    if x then
        return x, obj, y
    end

    return obj
end

local function third()
    local a1 <close> = func2close(function(_, msg)
        error(msg)
    end)
    local a2 <close> = setmetatable({}, { __close = print })
    local a3 <close> = func2close(function(_, msg)
        error(123)
    end)
    getmetatable(a2).__close = 4
end

local st1, msg1 = pcall(first)
local st2, msg2 = pcall(second)
local st3, msg3 = pcall(third)

return tostring(st1),
       string.find(msg1, "variable 'x' got a non%-closable value") ~= nil,
       tostring(st2),
       string.find(msg2, "metamethod 'close'") ~= nil,
       tostring(st3),
       string.find(msg3, "number value") ~= nil
""";

        var results = Execute(source);

        results.Length.ShouldBe(6);
        results[0].AsString().ShouldBe("false");
        results[1].AsBoolean().ShouldBeTrue();
        results[2].AsString().ShouldBe("false");
        results[3].AsBoolean().ShouldBeTrue();
        results[4].AsString().ShouldBe("false");
        results[5].AsBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldAnnotateStringCloseErrorsAsMetamethodFailures()
    {
        const string source = """
local debug = require("debug")

local function func2close(f, x, y)
    local obj = setmetatable({}, { __close = f })
    if x then
        return x, obj, y
    end

    return obj
end

local function foo(...)
    local x123 <close> = func2close(function()
        error("@x123")
    end)
end

local st, msg = xpcall(foo, debug.traceback)

return tostring(st),
       string.match(msg, "^[^ ]* @x123") ~= nil,
       string.find(msg, "in metamethod 'close'") ~= nil
""";

        var results = Execute(source);

        results.Length.ShouldBe(3);
        results[0].AsString().ShouldBe("false");
        results[1].AsBoolean().ShouldBeTrue();
        results[2].AsBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldTurnDeepRecursionIntoLuaStackOverflow()
    {
        const string source = """
local function overflow(n)
    return overflow(n + 1)
end

local function errorh(m)
    return string.find(m, "stack overflow") ~= nil
end

local st, handled = xpcall(overflow, errorh, 0)

return tostring(st), handled
""";

        var results = Execute(source);

        results.Length.ShouldBe(2);
        results[0].AsString().ShouldBe("false");
        results[1].AsBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldRunReturnHooksAfterCloseAndExposeConfiguredHook()
    {
        const string source = """
local debug = require("debug")

local function func2close(f)
    return setmetatable({}, { __close = f })
end

local trace = {}

local function hook(event)
    trace[#trace + 1] = event .. " " .. debug.getinfo(2).name
end

local function foo(...)
    local x <close> = func2close(function(_, msg)
        trace[#trace + 1] = "x"
    end)

    local y <close> = func2close(function(_, msg)
        debug.sethook(hook, "r")
    end)

    return ...
end

local t = { foo(10, 20, 30) }
debug.sethook()

return t[1], t[2], t[3], table.concat(trace, "|"), debug.gethook() == nil
""";

        var results = Execute(source);

        results.Length.ShouldBe(5);
        results[0].AsInteger().ShouldBe(10);
        results[1].AsInteger().ShouldBe(20);
        results[2].AsInteger().ShouldBe(30);
        results[3].AsString().ShouldBe("return sethook|return close|x|return close|return foo");
        results[4].AsBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Compile_ShouldResumeLuaCloseCallbacksWithoutLosingReturnValues()
    {
        const string source = """
local function func2close(f)
    return setmetatable({}, { __close = f })
end

local trace = {}
local co = coroutine.wrap(function ()
    local x <close> = func2close(function (_, msg)
        trace[#trace + 1] = "x1"
        coroutine.yield("pause")
        trace[#trace + 1] = "x2"
    end)

    return 10, 20
end)

local first = co()
local a, b = co()

return first, a, b, table.concat(trace, "|")
""";

        var results = Execute(source);

        results.Length.ShouldBe(4);
        results[0].AsString().ShouldBe("pause");
        results[1].AsInteger().ShouldBe(10);
        results[2].AsInteger().ShouldBe(20);
        results[3].AsString().ShouldBe("x1|x2");
    }

    [Fact]
    public void Compile_ShouldResumeScopeCloseBeforeContinuingExecution()
    {
        const string source = """
local function func2close(f)
    return setmetatable({}, { __close = f })
end

local trace = {}
local co = coroutine.wrap(function ()
    do
        local z <close> = func2close(function (_, msg)
            trace[#trace + 1] = "z1"
            coroutine.yield("scope")
            trace[#trace + 1] = "z2"
        end)
    end

    trace[#trace + 1] = "after"
    return 42
end)

local first = co()
local second = co()

return first, second, table.concat(trace, "|")
""";

        var results = Execute(source);

        results.Length.ShouldBe(3);
        results[0].AsString().ShouldBe("scope");
        results[1].AsInteger().ShouldBe(42);
        results[2].AsString().ShouldBe("z1|z2|after");
    }

    [Fact]
    public void Compile_ShouldResumeProtectedCallsAcrossYieldingCloseCallbacks()
    {
        const string source = """
local function func2close(f)
    return setmetatable({}, { __close = f })
end

local function foo()
    local z <close> = func2close(function (_, msg)
        coroutine.yield("z")
    end)

    local y <close> = func2close(function (_, msg)
        coroutine.yield("y")
    end)

    local x <close> = func2close(function (_, msg)
        coroutine.yield("x")
    end)

    return 10, 20
end

local co = coroutine.wrap(function ()
    return pcall(foo)
end)

local a = co()
local b = co()
local c = co()
local st, x, y = co()

return a, b, c, tostring(st), x, y
""";

        var results = Execute(source);

        results.Length.ShouldBe(6);
        results[0].AsString().ShouldBe("x");
        results[1].AsString().ShouldBe("y");
        results[2].AsString().ShouldBe("z");
        results[3].AsString().ShouldBe("true");
        results[4].AsInteger().ShouldBe(10);
        results[5].AsInteger().ShouldBe(20);
    }

    [Fact]
    public void Compile_ShouldPropagateLatestNestedCloseError()
    {
        const string source = """
local function func2close(f)
    return setmetatable({}, { __close = f })
end

local track = {}

local function foo()
    local x0 <close> = func2close(function(_, msg)
        track[#track + 1] = "x0:" .. tostring(msg)
    end)

    local x <close> = func2close(function()
        local xx <close> = func2close(function(_, msg)
            track[#track + 1] = "xx:" .. tostring(msg)
            error(202)
        end)

        track[#track + 1] = "x"
        error(101)
    end)

    track[#track + 1] = "foo"
    return 20, 30, 40
end

local st, msg = pcall(foo)
return tostring(st), tostring(msg), table.concat(track, "|")
""";

        var results = Execute(source);

        results.Length.ShouldBe(3);
        results[0].AsString().ShouldBe("false");
        results[1].AsString().ShouldBe("202");
        results[2].AsString().ShouldBe("foo|x|xx:101|x0:202");
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

    [Fact]
    public void Compile_ShouldPreserveDebugMetadataForNamedFunctions()
    {
        const string source = """
local env = 10
local foo = function ()
    return env
end

return foo
""";

        var vm = new LuaVirtualMachine();
        var chunk = LuaCompiler.Compile(source, "debug_names.lua");
        var debugGetInfo = vm.State.DebugLibrary.GetValue(LuaValue.FromString("getinfo")).AsFunction();
        var debugGetUpvalue = vm.State.DebugLibrary.GetValue(LuaValue.FromString("getupvalue")).AsFunction();
        var debugSetUpvalue = vm.State.DebugLibrary.GetValue(LuaValue.FromString("setupvalue")).AsFunction();

        var created = vm.Execute(chunk);
        var functionValue = created.ShouldHaveSingleItem();

        InvokeClosure(vm.State, debugGetInfo, functionValue, LuaValue.FromString("n"))
            .ShouldHaveSingleItem()
            .AsTable()
            .GetValue(LuaValue.FromString("name"))
            .IsNil.ShouldBeTrue();

        var getResult = InvokeClosure(vm.State, debugGetUpvalue, functionValue, LuaValue.FromInteger(1));
        getResult[0].AsString().ShouldBe("env");
        getResult[1].AsInteger().ShouldBe(10);

        InvokeClosure(vm.State, debugSetUpvalue, functionValue, LuaValue.FromInteger(1), LuaValue.FromInteger(25))
            .ShouldHaveSingleItem()
            .AsString()
            .ShouldBe("env");
        vm.Call(functionValue.AsFunction()).ShouldHaveSingleItem().AsInteger().ShouldBe(25);
    }

    private static LuaValue[] Execute(string source)
    {
        var vm = new LuaVirtualMachine();
        var chunk = LuaCompiler.Compile(source, "sample.lua");
        return vm.Execute(chunk);
    }

    private static LuaValue[] InvokeClosure(LuaState state, LuaClosure closure, params LuaValue[] arguments)
    {
        return closure.Body switch
        {
            LuaNativeClosureBody nativeBody => nativeBody.Function(state, closure, arguments),
            _ => throw new InvalidOperationException("Expected a native closure.")
        };
    }

    private static LuaClosure GetBaseFunction(LuaVirtualMachine vm, string name)
    {
        return vm.State.GlobalEnvironment.GetValue(LuaValue.FromString(name)).AsFunction();
    }
}
