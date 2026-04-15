using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Shouldly;
using System.Text;

namespace Lua.Runtime.Tests;

public class LuaStateTests
{
    [Fact]
    public void PushFrame_ShouldExposeCurrentFrame()
    {
        var state = new LuaState();
        var frame = new CallFrame(new LuaClosure("main"), baseIndex: 0, expectedResults: 1);

        state.PushFrame(frame);

        state.CurrentFrame.ShouldBeSameAs(frame);
        state.Frames.Count.ShouldBe(1);
    }

    [Fact]
    public void PopFrame_ShouldReturnLastFrame()
    {
        var state = new LuaState();
        var first = new CallFrame(new LuaClosure("first"), baseIndex: 0, expectedResults: 1);
        var second = new CallFrame(new LuaClosure("second"), baseIndex: 3, expectedResults: 2);

        state.PushFrame(first);
        state.PushFrame(second);

        state.PopFrame().ShouldBeSameAs(second);
        state.CurrentFrame.ShouldBeSameAs(first);
    }

    [Fact]
    public void LuaState_ShouldPreloadSetMetatable()
    {
        var state = new LuaState();

        var setMetatable = state.GlobalEnvironment.GetValue(LuaValue.FromString("setmetatable"));

        setMetatable.Kind.ShouldBe(LuaValueKind.Function);
        setMetatable.AsFunction().DebugName.ShouldBe("setmetatable");
    }

    [Fact]
    public void LuaState_ShouldPreloadError()
    {
        var state = new LuaState();

        var error = state.GlobalEnvironment.GetValue(LuaValue.FromString("error"));

        error.Kind.ShouldBe(LuaValueKind.Function);
        error.AsFunction().DebugName.ShouldBe("error");
    }

    [Fact]
    public void LuaState_ShouldPreloadBaseLibraryFunctions()
    {
        var state = new LuaState();

        state.GlobalEnvironment.GetValue(LuaValue.FromString("getmetatable")).AsFunction().DebugName.ShouldBe("getmetatable");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("rawequal")).AsFunction().DebugName.ShouldBe("rawequal");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("rawlen")).AsFunction().DebugName.ShouldBe("rawlen");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("rawget")).AsFunction().DebugName.ShouldBe("rawget");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("rawset")).AsFunction().DebugName.ShouldBe("rawset");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("next")).AsFunction().DebugName.ShouldBe("next");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("pairs")).AsFunction().DebugName.ShouldBe("pairs");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("ipairs")).AsFunction().DebugName.ShouldBe("ipairs");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("collectgarbage")).AsFunction().DebugName.ShouldBe("collectgarbage");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("load")).AsFunction().DebugName.ShouldBe("load");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("loadfile")).AsFunction().DebugName.ShouldBe("loadfile");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("dofile")).AsFunction().DebugName.ShouldBe("dofile");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("print")).AsFunction().DebugName.ShouldBe("print");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("warn")).AsFunction().DebugName.ShouldBe("warn");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("require")).AsFunction().DebugName.ShouldBe("require");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("type")).AsFunction().DebugName.ShouldBe("type");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("assert")).AsFunction().DebugName.ShouldBe("assert");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("select")).AsFunction().DebugName.ShouldBe("select");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("tonumber")).AsFunction().DebugName.ShouldBe("tonumber");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("tostring")).AsFunction().DebugName.ShouldBe("tostring");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("pcall")).AsFunction().DebugName.ShouldBe("pcall");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("xpcall")).AsFunction().DebugName.ShouldBe("xpcall");
    }

    [Fact]
    public void LuaState_ShouldPreloadMinimalPackageLibrary()
    {
        var state = new LuaState();

        var packageValue = state.GlobalEnvironment.GetValue(LuaValue.FromString("package"));
        packageValue.Kind.ShouldBe(LuaValueKind.Table);

        var packageTable = packageValue.AsTable();
        packageTable.ShouldBeSameAs(state.PackageLibrary);
        packageTable.GetValue(LuaValue.FromString("loaded")).AsTable().ShouldBeSameAs(state.PackageLoaded);
        packageTable.GetValue(LuaValue.FromString("preload")).AsTable().ShouldBeSameAs(state.PackagePreload);
        packageTable.GetValue(LuaValue.FromString("searchers")).AsTable().ShouldBeSameAs(state.PackageSearchers);
        packageTable.GetValue(LuaValue.FromString("path")).AsString().ShouldBe("./?.luac");

        state.PackageSearchers.GetValue(LuaValue.FromInteger(1)).AsFunction().DebugName.ShouldBe("package.searcher.preload");
        state.PackageSearchers.GetValue(LuaValue.FromInteger(2)).AsFunction().DebugName.ShouldBe("package.searcher.luac");
        state.PackageLoaded.GetValue(LuaValue.FromString("package")).AsTable().ShouldBeSameAs(state.PackageLibrary);
    }

    [Fact]
    public void LuaState_ShouldPreloadCoroutineLibrary()
    {
        var state = new LuaState();

        var coroutineValue = state.GlobalEnvironment.GetValue(LuaValue.FromString("coroutine"));
        coroutineValue.Kind.ShouldBe(LuaValueKind.Table);

        var coroutineTable = coroutineValue.AsTable();
        coroutineTable.ShouldBeSameAs(state.CoroutineLibrary);
        coroutineTable.GetValue(LuaValue.FromString("create")).AsFunction().DebugName.ShouldBe("coroutine.create");
        coroutineTable.GetValue(LuaValue.FromString("resume")).AsFunction().DebugName.ShouldBe("coroutine.resume");
        coroutineTable.GetValue(LuaValue.FromString("yield")).AsFunction().DebugName.ShouldBe("coroutine.yield");
        coroutineTable.GetValue(LuaValue.FromString("wrap")).AsFunction().DebugName.ShouldBe("coroutine.wrap");
        coroutineTable.GetValue(LuaValue.FromString("status")).AsFunction().DebugName.ShouldBe("coroutine.status");
        coroutineTable.GetValue(LuaValue.FromString("isyieldable")).AsFunction().DebugName.ShouldBe("coroutine.isyieldable");
        coroutineTable.GetValue(LuaValue.FromString("close")).AsFunction().DebugName.ShouldBe("coroutine.close");
        coroutineTable.GetValue(LuaValue.FromString("running")).AsFunction().DebugName.ShouldBe("coroutine.running");
    }

    [Fact]
    public void LuaState_ShouldPreloadMinimalStringLibraryAndMetatable()
    {
        var state = new LuaState();
        var getMetatable = GetBaseFunction(state, "getmetatable");

        var stringTableValue = state.GlobalEnvironment.GetValue(LuaValue.FromString("string"));
        stringTableValue.Kind.ShouldBe(LuaValueKind.Table);

        var stringTable = stringTableValue.AsTable();
        stringTable.ShouldBeSameAs(state.StringLibrary);
        stringTable.GetValue(LuaValue.FromString("byte")).AsFunction().DebugName.ShouldBe("string.byte");
        stringTable.GetValue(LuaValue.FromString("find")).AsFunction().DebugName.ShouldBe("string.find");
        stringTable.GetValue(LuaValue.FromString("format")).AsFunction().DebugName.ShouldBe("string.format");
        stringTable.GetValue(LuaValue.FromString("gsub")).AsFunction().DebugName.ShouldBe("string.gsub");
        stringTable.GetValue(LuaValue.FromString("upper")).AsFunction().DebugName.ShouldBe("string.upper");
        stringTable.GetValue(LuaValue.FromString("lower")).AsFunction().DebugName.ShouldBe("string.lower");
        stringTable.GetValue(LuaValue.FromString("len")).AsFunction().DebugName.ShouldBe("string.len");
        stringTable.GetValue(LuaValue.FromString("pack")).AsFunction().DebugName.ShouldBe("string.pack");
        stringTable.GetValue(LuaValue.FromString("unpack")).AsFunction().DebugName.ShouldBe("string.unpack");

        var stringMetatable = InvokeBaseFunction(state, getMetatable, LuaValue.FromString("lua"))
            .ShouldHaveSingleItem()
            .AsTable();
        stringMetatable.GetValue(LuaValue.FromString("__index")).AsTable().ShouldBeSameAs(state.StringLibrary);
        stringMetatable.GetValue(LuaValue.FromString("__add")).AsFunction().DebugName.ShouldBe("__add");
        stringMetatable.GetValue(LuaValue.FromString("__unm")).AsFunction().DebugName.ShouldBe("__unm");
    }

    [Fact]
    public void StringLibrary_ShouldHandleByteStringOperations()
    {
        var state = new LuaState();
        var byteFunction = GetLibraryFunction(state.StringLibrary, "byte");
        var charFunction = GetLibraryFunction(state.StringLibrary, "char");
        var repFunction = GetLibraryFunction(state.StringLibrary, "rep");
        var reverseFunction = GetLibraryFunction(state.StringLibrary, "reverse");
        var subFunction = GetLibraryFunction(state.StringLibrary, "sub");
        var lenFunction = GetLibraryFunction(state.StringLibrary, "len");

        InvokeClosure(state, byteFunction, LuaValue.FromString("Aπ"), LuaValue.FromInteger(1), LuaValue.FromInteger(3))
            .ShouldBe([LuaValue.FromInteger(65), LuaValue.FromInteger(207), LuaValue.FromInteger(128)]);

        InvokeClosure(state, charFunction, LuaValue.FromInteger(65), LuaValue.FromInteger(207), LuaValue.FromInteger(128))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("Aπ");

        InvokeClosure(state, repFunction, LuaValue.FromString("ab"), LuaValue.FromInteger(3), LuaValue.FromString("-"))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("ab-ab-ab");

        InvokeClosure(state, reverseFunction, LuaValue.FromString("abc"))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("cba");

        InvokeClosure(state, subFunction, LuaValue.FromString("Aπ"), LuaValue.FromInteger(2), LuaValue.FromInteger(-1))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("π");

        InvokeClosure(state, lenFunction, LuaValue.FromString("Aπ"))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(3);
    }

    [Fact]
    public void StringLibrary_ShouldHandlePatternsAndReplacement()
    {
        var state = new LuaState();
        var findFunction = GetLibraryFunction(state.StringLibrary, "find");
        var matchFunction = GetLibraryFunction(state.StringLibrary, "match");
        var gmatchFunction = GetLibraryFunction(state.StringLibrary, "gmatch");
        var gsubFunction = GetLibraryFunction(state.StringLibrary, "gsub");

        InvokeClosure(state, findFunction, LuaValue.FromString("hello 123"), LuaValue.FromString("%d+"))
            .ShouldBe([LuaValue.FromInteger(7), LuaValue.FromInteger(9)]);

        InvokeClosure(state, findFunction, LuaValue.FromString("banana"), LuaValue.FromString("na"), LuaValue.FromInteger(3), LuaValue.FromBoolean(true))
            .ShouldBe([LuaValue.FromInteger(3), LuaValue.FromInteger(4)]);

        InvokeClosure(state, matchFunction, LuaValue.FromString("abc 42"), LuaValue.FromString("(%a+)%s+(%d+)"))
            .ShouldBe([LuaValue.FromString("abc"), LuaValue.FromString("42")]);

        InvokeClosure(state, matchFunction, LuaValue.FromString("aabb"), LuaValue.FromString("()bb()"))
            .ShouldBe([LuaValue.FromInteger(3), LuaValue.FromInteger(5)]);

        InvokeClosure(state, matchFunction, LuaValue.FromString("xx(a(b)c)yy"), LuaValue.FromString("%b()"))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("(a(b)c)");

        var replacementTable = new LuaTable();
        replacementTable.SetValue(LuaValue.FromString("cat"), LuaValue.FromString("animal"));
        replacementTable.SetValue(LuaValue.FromString("dog"), LuaValue.FromString("pet"));
        InvokeClosure(state, gsubFunction, LuaValue.FromString("cat 42 dog"), LuaValue.FromString("(%a+)"), LuaValue.FromTable(replacementTable))
            .ShouldBe([LuaValue.FromString("animal 42 pet"), LuaValue.FromInteger(2)]);

        var iterator = InvokeClosure(state, gmatchFunction, LuaValue.FromString("x=10,y=20"), LuaValue.FromString("(%a)=(%d+)"))
            .ShouldHaveSingleItem()
            .AsFunction();
        InvokeClosure(state, iterator).ShouldBe([LuaValue.FromString("x"), LuaValue.FromString("10")]);
        InvokeClosure(state, iterator).ShouldBe([LuaValue.FromString("y"), LuaValue.FromString("20")]);
        InvokeClosure(state, iterator).ShouldBeEmpty();
    }

    [Fact]
    public void StringLibrary_ShouldHandleFormatAndPack()
    {
        var state = new LuaState();
        var formatFunction = GetLibraryFunction(state.StringLibrary, "format");
        var packFunction = GetLibraryFunction(state.StringLibrary, "pack");
        var packSizeFunction = GetLibraryFunction(state.StringLibrary, "packsize");
        var unpackFunction = GetLibraryFunction(state.StringLibrary, "unpack");
        var byteFunction = GetLibraryFunction(state.StringLibrary, "byte");

        InvokeClosure(
            state,
            formatFunction,
            LuaValue.FromString("%s|%d|%.2f|%q"),
            LuaValue.FromString("lua"),
            LuaValue.FromInteger(7),
            LuaValue.FromFloat(2.5d),
            LuaValue.FromString("a\nb"))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("lua|7|2.50|\"a\\nb\"");

        var packed = InvokeClosure(
            state,
            packFunction,
            LuaValue.FromString("<I2I2"),
            LuaValue.FromInteger(513),
            LuaValue.FromInteger(1027))
            .ShouldHaveSingleItem();

        InvokeClosure(state, packSizeFunction, LuaValue.FromString("<I2I2"))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(4);

        InvokeClosure(state, byteFunction, packed, LuaValue.FromInteger(1), LuaValue.FromInteger(4))
            .ShouldBe([LuaValue.FromInteger(1), LuaValue.FromInteger(2), LuaValue.FromInteger(3), LuaValue.FromInteger(4)]);

        InvokeClosure(state, unpackFunction, LuaValue.FromString("<I2I2"), packed)
            .ShouldBe([LuaValue.FromInteger(513), LuaValue.FromInteger(1027), LuaValue.FromInteger(5)]);
    }

    [Fact]
    public void LuaState_ShouldPreloadTableMathAndUtf8Libraries()
    {
        var state = new LuaState();

        var tableTable = state.GlobalEnvironment.GetValue(LuaValue.FromString("table")).AsTable();
        tableTable.ShouldBeSameAs(state.TableLibrary);
        tableTable.GetValue(LuaValue.FromString("concat")).AsFunction().DebugName.ShouldBe("table.concat");
        tableTable.GetValue(LuaValue.FromString("sort")).AsFunction().DebugName.ShouldBe("table.sort");
        tableTable.GetValue(LuaValue.FromString("unpack")).AsFunction().DebugName.ShouldBe("table.unpack");

        var mathTable = state.GlobalEnvironment.GetValue(LuaValue.FromString("math")).AsTable();
        mathTable.ShouldBeSameAs(state.MathLibrary);
        mathTable.GetValue(LuaValue.FromString("abs")).AsFunction().DebugName.ShouldBe("math.abs");
        mathTable.GetValue(LuaValue.FromString("atan")).AsFunction().DebugName.ShouldBe("math.atan");
        mathTable.GetValue(LuaValue.FromString("pi")).AsFloat().ShouldBe(Math.PI, 1e-12);
        mathTable.GetValue(LuaValue.FromString("maxinteger")).AsInteger().ShouldBe(long.MaxValue);
        mathTable.GetValue(LuaValue.FromString("mininteger")).AsInteger().ShouldBe(long.MinValue);

        var utf8Table = state.GlobalEnvironment.GetValue(LuaValue.FromString("utf8")).AsTable();
        utf8Table.ShouldBeSameAs(state.Utf8Library);
        utf8Table.GetValue(LuaValue.FromString("offset")).AsFunction().DebugName.ShouldBe("utf8.offset");
        utf8Table.GetValue(LuaValue.FromString("codes")).AsFunction().DebugName.ShouldBe("utf8.codes");
        utf8Table.GetValue(LuaValue.FromString("charpattern")).AsString().ShouldNotBeEmpty();
    }

    [Fact]
    public void GetMetatable_ShouldRespectProtectedFieldForTableAndUserData()
    {
        var state = new LuaState();
        var getMetatable = GetBaseFunction(state, "getmetatable");
        var protectedTable = new LuaTable();
        var tableMetatable = new LuaTable();
        var userData = new LuaUserData(new object());
        var userDataMetatable = new LuaTable();

        tableMetatable.SetValue(LuaValue.FromString("__metatable"), LuaValue.FromString("locked-table"));
        protectedTable.SetMetatable(tableMetatable);
        userDataMetatable.SetValue(LuaValue.FromString("__metatable"), LuaValue.FromBoolean(false));
        userData.SetMetatable(userDataMetatable);

        InvokeBaseFunction(state, getMetatable, LuaValue.FromTable(protectedTable))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("locked-table");

        InvokeBaseFunction(state, getMetatable, LuaValue.FromUserData(userData))
            .ShouldHaveSingleItem()
            .AsBoolean().ShouldBeFalse();
    }

    [Fact]
    public void SetMetatable_ShouldRejectProtectedMetatable()
    {
        var state = new LuaState();
        var setMetatable = GetBaseFunction(state, "setmetatable");
        var table = new LuaTable();
        var metatable = new LuaTable();

        metatable.SetValue(LuaValue.FromString("__metatable"), LuaValue.FromString("locked"));
        table.SetMetatable(metatable);

        var exception = Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, setMetatable, LuaValue.FromTable(table), LuaValue.FromTable(new LuaTable())));

        exception.ErrorObject.AsString().ShouldBe("cannot change a protected metatable");
    }

    [Fact]
    public void RawGet_ShouldReturnNilForNilAndNaNKeys()
    {
        var state = new LuaState();
        var rawGet = GetBaseFunction(state, "rawget");
        var table = new LuaTable();

        table.SetValue(LuaValue.FromString("answer"), LuaValue.FromInteger(42));

        InvokeBaseFunction(state, rawGet, LuaValue.FromTable(table), LuaValue.Nil)
            .ShouldHaveSingleItem()
            .IsNil.ShouldBeTrue();

        InvokeBaseFunction(state, rawGet, LuaValue.FromTable(table), LuaValue.FromFloat(double.NaN))
            .ShouldHaveSingleItem()
            .IsNil.ShouldBeTrue();
    }

    [Fact]
    public void RawSetRawLenAndRawEqual_ShouldUseRawSemantics()
    {
        var state = new LuaState();
        var rawSet = GetBaseFunction(state, "rawset");
        var rawLen = GetBaseFunction(state, "rawlen");
        var rawEqual = GetBaseFunction(state, "rawequal");
        var table = new LuaTable();

        table.SetValue(LuaValue.FromInteger(1), LuaValue.FromString("a"));
        table.SetValue(LuaValue.FromInteger(2), LuaValue.FromString("b"));

        InvokeBaseFunction(state, rawSet, LuaValue.FromTable(table), LuaValue.FromString("answer"), LuaValue.FromInteger(42))
            .ShouldHaveSingleItem()
            .AsTable().ShouldBeSameAs(table);
        table.GetValue(LuaValue.FromString("answer")).AsInteger().ShouldBe(42);

        InvokeBaseFunction(state, rawLen, LuaValue.FromTable(table))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(2);
        InvokeBaseFunction(state, rawLen, LuaValue.FromString("lua"))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(3);

        InvokeBaseFunction(state, rawEqual, LuaValue.FromInteger(1), LuaValue.FromFloat(1.0))
            .ShouldHaveSingleItem()
            .AsBoolean().ShouldBeTrue();
        InvokeBaseFunction(state, rawEqual, LuaValue.FromTable(table), LuaValue.FromTable(new LuaTable()))
            .ShouldHaveSingleItem()
            .AsBoolean().ShouldBeFalse();
    }

    [Fact]
    public void TypeAssertAndSelect_ShouldUseLuaSemantics()
    {
        var state = new LuaState();
        var type = GetBaseFunction(state, "type");
        var assert = GetBaseFunction(state, "assert");
        var select = GetBaseFunction(state, "select");
        var userData = new LuaUserData(new object());

        InvokeBaseFunction(state, type, LuaValue.Nil)
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("nil");
        InvokeBaseFunction(state, type, LuaValue.FromInteger(1))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("number");
        InvokeBaseFunction(state, type, LuaValue.FromUserData(userData))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("userdata");

        var assertResults = InvokeBaseFunction(
            state,
            assert,
            LuaValue.FromString("ok"),
            LuaValue.FromInteger(1),
            LuaValue.FromInteger(2));
        assertResults.Length.ShouldBe(3);
        assertResults[0].AsString().ShouldBe("ok");
        assertResults[1].AsInteger().ShouldBe(1);
        assertResults[2].AsInteger().ShouldBe(2);

        var assertException = Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, assert, LuaValue.FromBoolean(false), LuaValue.FromString("boom")));
        assertException.ErrorObject.AsString().ShouldBe("boom");

        InvokeBaseFunction(
                state,
                select,
                LuaValue.FromString("#"),
                LuaValue.FromInteger(10),
                LuaValue.FromInteger(20),
                LuaValue.FromInteger(30))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(3);

        var selectedValues = InvokeBaseFunction(
            state,
            select,
            LuaValue.FromInteger(-2),
            LuaValue.FromInteger(10),
            LuaValue.FromInteger(20),
            LuaValue.FromInteger(30));
        selectedValues.Length.ShouldBe(2);
        selectedValues[0].AsInteger().ShouldBe(20);
        selectedValues[1].AsInteger().ShouldBe(30);
    }

    [Fact]
    public void NextPairsAndIPairs_ShouldUseLuaSemantics()
    {
        var state = new LuaState();
        state.SetCallableInvoker((callable, arguments) =>
        {
            var closure = callable.AsFunction();
            var body = (LuaNativeClosureBody)closure.Body!;
            return body.Function(state, closure, arguments);
        });

        var next = GetBaseFunction(state, "next");
        var pairs = GetBaseFunction(state, "pairs");
        var ipairs = GetBaseFunction(state, "ipairs");
        var single = new LuaTable();
        var customTable = new LuaTable();
        var customMetatable = new LuaTable();
        var array = new LuaTable();

        single.SetValue(LuaValue.FromString("only"), LuaValue.FromInteger(42));

        var nextResults = InvokeBaseFunction(state, next, LuaValue.FromTable(single));
        nextResults.Length.ShouldBe(2);
        nextResults[0].AsString().ShouldBe("only");
        nextResults[1].AsInteger().ShouldBe(42);

        InvokeBaseFunction(state, next, LuaValue.FromTable(single), nextResults[0])
            .ShouldHaveSingleItem()
            .IsNil.ShouldBeTrue();

        var pairResults = InvokeBaseFunction(state, pairs, LuaValue.FromTable(single));
        pairResults.Length.ShouldBe(4);
        pairResults[0].AsFunction().DebugName.ShouldBe("next");
        pairResults[1].ShouldBe(LuaValue.FromTable(single));
        pairResults[2].IsNil.ShouldBeTrue();
        pairResults[3].IsNil.ShouldBeTrue();

        customMetatable.SetValue(
            LuaValue.FromString("__pairs"),
            LuaValue.FromFunction(new LuaClosure(
                "__pairs",
                body: new LuaNativeClosureBody(static (_, _, arguments) =>
                [
                    LuaValue.FromFunction(new LuaClosure(
                        "custom-iter",
                        body: new LuaNativeClosureBody(static (_, _, iterArguments) => iterArguments[1].IsNil
                            ? [LuaValue.FromString("tag"), LuaValue.FromInteger(99)]
                            : [LuaValue.Nil]))),
                    arguments[0],
                    LuaValue.Nil
                ]))));
        customTable.SetMetatable(customMetatable);

        var customPairResults = InvokeBaseFunction(state, pairs, LuaValue.FromTable(customTable));
        customPairResults.Length.ShouldBe(4);
        customPairResults[0].AsFunction().DebugName.ShouldBe("custom-iter");
        customPairResults[1].ShouldBe(LuaValue.FromTable(customTable));
        customPairResults[2].IsNil.ShouldBeTrue();
        customPairResults[3].IsNil.ShouldBeTrue();

        array.SetValue(LuaValue.FromInteger(1), LuaValue.FromInteger(10));
        array.SetValue(LuaValue.FromInteger(2), LuaValue.FromInteger(20));
        array.SetValue(LuaValue.FromInteger(4), LuaValue.FromInteger(40));

        var ipairsResults = InvokeBaseFunction(state, ipairs, LuaValue.FromTable(array));
        ipairsResults.Length.ShouldBe(3);
        ipairsResults[0].AsFunction().DebugName.ShouldBe("ipairsaux");
        ipairsResults[1].ShouldBe(LuaValue.FromTable(array));
        ipairsResults[2].AsInteger().ShouldBe(0);

        var first = state.InvokeCallable(ipairsResults[0], [ipairsResults[1], ipairsResults[2]]);
        first.Length.ShouldBe(2);
        first[0].AsInteger().ShouldBe(1);
        first[1].AsInteger().ShouldBe(10);

        var second = state.InvokeCallable(ipairsResults[0], [ipairsResults[1], first[0]]);
        second.Length.ShouldBe(2);
        second[0].AsInteger().ShouldBe(2);
        second[1].AsInteger().ShouldBe(20);

        state.InvokeCallable(ipairsResults[0], [ipairsResults[1], second[0]])
            .ShouldHaveSingleItem()
            .IsNil.ShouldBeTrue();
    }

    [Fact]
    public void TableLibrary_ShouldSupportCoreOperations()
    {
        var state = new LuaState();
        state.SetCallableInvoker((callable, arguments) =>
        {
            var closure = callable.AsFunction();
            var body = (LuaNativeClosureBody)closure.Body!;
            return body.Function(state, closure, arguments);
        });

        var concat = state.TableLibrary.GetValue(LuaValue.FromString("concat")).AsFunction();
        var insert = state.TableLibrary.GetValue(LuaValue.FromString("insert")).AsFunction();
        var remove = state.TableLibrary.GetValue(LuaValue.FromString("remove")).AsFunction();
        var move = state.TableLibrary.GetValue(LuaValue.FromString("move")).AsFunction();
        var sort = state.TableLibrary.GetValue(LuaValue.FromString("sort")).AsFunction();
        var pack = state.TableLibrary.GetValue(LuaValue.FromString("pack")).AsFunction();
        var unpack = state.TableLibrary.GetValue(LuaValue.FromString("unpack")).AsFunction();

        var concatTable = new LuaTable();
        concatTable.SetValue(LuaValue.FromInteger(1), LuaValue.FromString("a"));
        concatTable.SetValue(LuaValue.FromInteger(2), LuaValue.FromString("b"));
        concatTable.SetValue(LuaValue.FromInteger(3), LuaValue.FromInteger(3));
        InvokeBaseFunction(state, concat, LuaValue.FromTable(concatTable), LuaValue.FromString("-"))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("a-b-3");

        var sequence = new LuaTable();
        sequence.SetValue(LuaValue.FromInteger(1), LuaValue.FromInteger(10));
        sequence.SetValue(LuaValue.FromInteger(2), LuaValue.FromInteger(20));
        sequence.SetValue(LuaValue.FromInteger(3), LuaValue.FromInteger(30));

        InvokeBaseFunction(state, insert, LuaValue.FromTable(sequence), LuaValue.FromInteger(2), LuaValue.FromInteger(15));
        sequence.GetValue(LuaValue.FromInteger(1)).AsInteger().ShouldBe(10);
        sequence.GetValue(LuaValue.FromInteger(2)).AsInteger().ShouldBe(15);
        sequence.GetValue(LuaValue.FromInteger(3)).AsInteger().ShouldBe(20);
        sequence.GetValue(LuaValue.FromInteger(4)).AsInteger().ShouldBe(30);

        InvokeBaseFunction(state, remove, LuaValue.FromTable(sequence), LuaValue.FromInteger(4))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(30);
        sequence.GetValue(LuaValue.FromInteger(3)).AsInteger().ShouldBe(20);
        sequence.GetValue(LuaValue.FromInteger(4)).IsNil.ShouldBeTrue();

        var destination = new LuaTable();
        InvokeBaseFunction(
                state,
                move,
                LuaValue.FromTable(sequence),
                LuaValue.FromInteger(1),
                LuaValue.FromInteger(3),
                LuaValue.FromInteger(2),
                LuaValue.FromTable(destination))
            .ShouldHaveSingleItem()
            .AsTable().ShouldBeSameAs(destination);
        destination.GetValue(LuaValue.FromInteger(2)).AsInteger().ShouldBe(10);
        destination.GetValue(LuaValue.FromInteger(3)).AsInteger().ShouldBe(15);
        destination.GetValue(LuaValue.FromInteger(4)).AsInteger().ShouldBe(20);

        var sortable = new LuaTable();
        sortable.SetValue(LuaValue.FromInteger(1), LuaValue.FromInteger(5));
        sortable.SetValue(LuaValue.FromInteger(2), LuaValue.FromInteger(2));
        sortable.SetValue(LuaValue.FromInteger(3), LuaValue.FromInteger(8));
        sortable.SetValue(LuaValue.FromInteger(4), LuaValue.FromInteger(1));
        var descending = new LuaClosure(
            "descending",
            body: new LuaNativeClosureBody(static (_, _, arguments) =>
            [
                LuaValue.FromBoolean(arguments[0].AsInteger() > arguments[1].AsInteger())
            ]));

        InvokeBaseFunction(state, sort, LuaValue.FromTable(sortable), LuaValue.FromFunction(descending));
        sortable.GetValue(LuaValue.FromInteger(1)).AsInteger().ShouldBe(8);
        sortable.GetValue(LuaValue.FromInteger(2)).AsInteger().ShouldBe(5);
        sortable.GetValue(LuaValue.FromInteger(3)).AsInteger().ShouldBe(2);
        sortable.GetValue(LuaValue.FromInteger(4)).AsInteger().ShouldBe(1);

        var packed = InvokeBaseFunction(
                state,
                pack,
                LuaValue.FromString("x"),
                LuaValue.Nil,
                LuaValue.FromString("z"))
            .ShouldHaveSingleItem()
            .AsTable();
        packed.GetValue(LuaValue.FromString("n")).AsInteger().ShouldBe(3);

        var unpacked = InvokeBaseFunction(
            state,
            unpack,
            LuaValue.FromTable(packed),
            LuaValue.FromInteger(1),
            LuaValue.FromInteger(3));
        unpacked.Length.ShouldBe(3);
        unpacked[0].AsString().ShouldBe("x");
        unpacked[1].IsNil.ShouldBeTrue();
        unpacked[2].AsString().ShouldBe("z");
    }

    [Fact]
    public void MathLibrary_ShouldSupportCoreOperations()
    {
        var state = new LuaState();
        var abs = state.MathLibrary.GetValue(LuaValue.FromString("abs")).AsFunction();
        var ceil = state.MathLibrary.GetValue(LuaValue.FromString("ceil")).AsFunction();
        var floor = state.MathLibrary.GetValue(LuaValue.FromString("floor")).AsFunction();
        var max = state.MathLibrary.GetValue(LuaValue.FromString("max")).AsFunction();
        var min = state.MathLibrary.GetValue(LuaValue.FromString("min")).AsFunction();
        var sqrt = state.MathLibrary.GetValue(LuaValue.FromString("sqrt")).AsFunction();
        var log = state.MathLibrary.GetValue(LuaValue.FromString("log")).AsFunction();
        var sin = state.MathLibrary.GetValue(LuaValue.FromString("sin")).AsFunction();
        var atan = state.MathLibrary.GetValue(LuaValue.FromString("atan")).AsFunction();
        var fmod = state.MathLibrary.GetValue(LuaValue.FromString("fmod")).AsFunction();
        var modf = state.MathLibrary.GetValue(LuaValue.FromString("modf")).AsFunction();
        var tointeger = state.MathLibrary.GetValue(LuaValue.FromString("tointeger")).AsFunction();
        var type = state.MathLibrary.GetValue(LuaValue.FromString("type")).AsFunction();
        var ult = state.MathLibrary.GetValue(LuaValue.FromString("ult")).AsFunction();

        InvokeBaseFunction(state, abs, LuaValue.FromInteger(-5))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(5);
        InvokeBaseFunction(state, ceil, LuaValue.FromFloat(2.2))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(3);
        InvokeBaseFunction(state, floor, LuaValue.FromFloat(2.8))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(2);
        InvokeBaseFunction(state, max, LuaValue.FromInteger(1), LuaValue.FromInteger(9), LuaValue.FromInteger(3))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(9);
        InvokeBaseFunction(state, min, LuaValue.FromInteger(1), LuaValue.FromInteger(9), LuaValue.FromInteger(3))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(1);
        InvokeBaseFunction(state, sqrt, LuaValue.FromInteger(81))
            .ShouldHaveSingleItem()
            .AsFloat().ShouldBe(9d, 1e-12);
        InvokeBaseFunction(state, log, LuaValue.FromFloat(Math.Exp(3d)))
            .ShouldHaveSingleItem()
            .AsFloat().ShouldBe(3d, 1e-12);
        InvokeBaseFunction(state, sin, LuaValue.FromFloat(Math.PI / 2d))
            .ShouldHaveSingleItem()
            .AsFloat().ShouldBe(1d, 1e-12);
        InvokeBaseFunction(state, atan, LuaValue.FromInteger(1), LuaValue.FromInteger(1))
            .ShouldHaveSingleItem()
            .AsFloat().ShouldBe(Math.PI / 4d, 1e-12);
        InvokeBaseFunction(state, fmod, LuaValue.FromInteger(17), LuaValue.FromInteger(5))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(2);

        var modfResults = InvokeBaseFunction(state, modf, LuaValue.FromFloat(-3.75));
        modfResults.Length.ShouldBe(2);
        modfResults[0].AsInteger().ShouldBe(-3);
        modfResults[1].AsFloat().ShouldBe(-0.75, 1e-12);

        InvokeBaseFunction(state, tointeger, LuaValue.FromFloat(9d))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(9);
        InvokeBaseFunction(state, type, LuaValue.FromInteger(1))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("integer");
        InvokeBaseFunction(state, type, LuaValue.FromFloat(1.5))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("float");
        InvokeBaseFunction(state, ult, LuaValue.FromInteger(0), LuaValue.FromInteger(-1))
            .ShouldHaveSingleItem()
            .AsBoolean().ShouldBeTrue();

        state.MathLibrary.GetValue(LuaValue.FromString("huge")).AsFloat().ShouldBe(double.PositiveInfinity);
        state.MathLibrary.GetValue(LuaValue.FromString("maxinteger")).AsInteger().ShouldBe(long.MaxValue);
        state.MathLibrary.GetValue(LuaValue.FromString("mininteger")).AsInteger().ShouldBe(long.MinValue);
    }

    [Fact]
    public void Utf8Library_ShouldSupportCoreOperations()
    {
        var state = new LuaState();
        var offset = state.Utf8Library.GetValue(LuaValue.FromString("offset")).AsFunction();
        var codepoint = state.Utf8Library.GetValue(LuaValue.FromString("codepoint")).AsFunction();
        var charFunction = state.Utf8Library.GetValue(LuaValue.FromString("char")).AsFunction();
        var len = state.Utf8Library.GetValue(LuaValue.FromString("len")).AsFunction();
        var codes = state.Utf8Library.GetValue(LuaValue.FromString("codes")).AsFunction();
        var text = LuaValue.FromString("Aπ文");

        var firstOffset = InvokeBaseFunction(state, offset, text, LuaValue.FromInteger(1), LuaValue.FromInteger(1));
        firstOffset.Length.ShouldBe(2);
        firstOffset[0].AsInteger().ShouldBe(1);
        firstOffset[1].AsInteger().ShouldBe(1);

        var secondOffset = InvokeBaseFunction(state, offset, text, LuaValue.FromInteger(2), LuaValue.FromInteger(1));
        secondOffset.Length.ShouldBe(2);
        secondOffset[0].AsInteger().ShouldBe(2);
        secondOffset[1].AsInteger().ShouldBe(3);

        var codepoints = InvokeBaseFunction(state, codepoint, text, LuaValue.FromInteger(1), LuaValue.FromInteger(-1));
        codepoints.Length.ShouldBe(3);
        codepoints[0].AsInteger().ShouldBe(65);
        codepoints[1].AsInteger().ShouldBe(960);
        codepoints[2].AsInteger().ShouldBe(25991);

        InvokeBaseFunction(state, charFunction, LuaValue.FromInteger(65), LuaValue.FromInteger(960), LuaValue.FromInteger(25991))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("Aπ文");

        InvokeBaseFunction(state, len, text)
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(3);

        var codesState = InvokeBaseFunction(state, codes, text);
        codesState.Length.ShouldBe(3);
        var iterator = codesState[0].AsFunction();

        var first = InvokeBaseFunction(state, iterator, text, LuaValue.FromInteger(0));
        first.Length.ShouldBe(2);
        first[0].AsInteger().ShouldBe(1);
        first[1].AsInteger().ShouldBe(65);

        var second = InvokeBaseFunction(state, iterator, text, first[0]);
        second.Length.ShouldBe(2);
        second[0].AsInteger().ShouldBe(2);
        second[1].AsInteger().ShouldBe(960);

        var third = InvokeBaseFunction(state, iterator, text, second[0]);
        third.Length.ShouldBe(2);
        third[0].AsInteger().ShouldBe(4);
        third[1].AsInteger().ShouldBe(25991);

        InvokeBaseFunction(state, iterator, text, third[0]).ShouldBeEmpty();
    }

    [Fact]
    public void Next_ShouldRejectInvalidCurrentKey()
    {
        var state = new LuaState();
        var next = GetBaseFunction(state, "next");
        var table = new LuaTable();

        table.SetValue(LuaValue.FromString("only"), LuaValue.FromInteger(42));

        var exception = Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, next, LuaValue.FromTable(table), LuaValue.FromString("missing")));

        exception.ErrorObject.AsString().ShouldBe("invalid key to 'next'");
    }

    [Fact]
    public void PrintAndWarn_ShouldUseLuaSemantics()
    {
        var state = new LuaState();
        state.SetCallableInvoker((callable, arguments) =>
        {
            var closure = callable.AsFunction();
            var body = (LuaNativeClosureBody)closure.Body!;
            return body.Function(state, closure, arguments);
        });

        var print = GetBaseFunction(state, "print");
        var warn = GetBaseFunction(state, "warn");
        var lines = new List<string>();
        var warnings = new List<string>();
        var table = new LuaTable();
        var metatable = new LuaTable();

        state.PrintOutput = lines.Add;
        state.WarningOutput = warnings.Add;

        metatable.SetValue(
            LuaValue.FromString("__tostring"),
            LuaValue.FromFunction(new LuaClosure(
                "__tostring",
                body: new LuaNativeClosureBody(static (_, _, _) => [LuaValue.FromString("obj")]))));
        table.SetMetatable(metatable);

        InvokeBaseFunction(
            state,
            print,
            LuaValue.FromString("head"),
            LuaValue.FromInteger(42),
            LuaValue.FromTable(table))
            .ShouldBeEmpty();

        InvokeBaseFunction(state, warn, LuaValue.FromString("hidden")).ShouldBeEmpty();
        InvokeBaseFunction(state, warn, LuaValue.FromString("@on")).ShouldBeEmpty();
        InvokeBaseFunction(
            state,
            warn,
            LuaValue.FromString("a"),
            LuaValue.FromInteger(42),
            LuaValue.FromString("z"))
            .ShouldBeEmpty();
        InvokeBaseFunction(state, warn, LuaValue.FromString("@off")).ShouldBeEmpty();
        InvokeBaseFunction(state, warn, LuaValue.FromString("hidden-again")).ShouldBeEmpty();

        lines.ShouldHaveSingleItem();
        lines[0].ShouldBe("head\t42\tobj");
        warnings.ShouldHaveSingleItem();
        warnings[0].ShouldBe("Lua warning: a42z");
    }

    [Fact]
    public void LoadLoadFileAndDoFile_ShouldUseBinaryChunkSemantics()
    {
        var state = new LuaState();
        state.SetCallableInvoker((callable, arguments) =>
        {
            var closure = callable.AsFunction();
            var body = (LuaNativeClosureBody)closure.Body!;
            return body.Function(state, closure, arguments);
        });

        var load = GetBaseFunction(state, "load");
        var loadfile = GetBaseFunction(state, "loadfile");
        var dofile = GetBaseFunction(state, "dofile");
        var envTable = new LuaTable();
        var chunkBytes = new byte[] { 0x1B, (byte)'L', (byte)'u', (byte)'a', 0x55, 0x66 };
        var chunkText = Encoding.Latin1.GetString(chunkBytes);
        var readerPieces = new Queue<LuaValue>(
        [
            LuaValue.FromString(chunkText[..3]),
            LuaValue.FromString(chunkText[3..]),
            LuaValue.Nil
        ]);
        var loaded = new List<(string? ChunkName, bool HasEnvironment, LuaValue Environment, byte[] Bytes)>();

        state.SetBinaryChunkLoader((bytes, chunkName, hasEnvironment, environment) =>
        {
            loaded.Add((chunkName, hasEnvironment, environment, bytes.ToArray()));
            return new LuaClosure(
                chunkName ?? "loaded",
                upvalueCount: 1,
                body: new LuaNativeClosureBody((innerState, closure, _) =>
                [
                    LuaValue.FromString(closure.DebugName ?? "loaded"),
                    LuaValue.FromInteger(closure.Upvalues[0].GetValue(innerState).AsInteger())
                ]),
                upvalues:
                [
                    new LuaUpvalue(hasEnvironment ? environment : LuaValue.FromInteger(-1))
                ]);
        });
        state.FileReader = path =>
        {
            path.ShouldBe("fixture.luac");
            return chunkBytes;
        };

        var fromString = InvokeBaseFunction(
            state,
            load,
            LuaValue.FromString(chunkText),
            LuaValue.FromString("=(string)"),
            LuaValue.FromString("b"),
            LuaValue.FromInteger(41));
        fromString.Length.ShouldBe(1);
        fromString[0].Kind.ShouldBe(LuaValueKind.Function);
        state.InvokeCallable(fromString[0], []).ShouldBe(
        [
            LuaValue.FromString("=(string)"),
            LuaValue.FromInteger(41)
        ]);

        var reader = new LuaClosure(
            "reader",
            body: new LuaNativeClosureBody((_, _, _) =>
            {
                var next = readerPieces.Dequeue();
                return [next];
            }));
        var fromReader = InvokeBaseFunction(
            state,
            load,
            LuaValue.FromFunction(reader),
            LuaValue.FromString("=(reader)"),
            LuaValue.FromString("b"));
        fromReader.Length.ShouldBe(1);
        state.InvokeCallable(fromReader[0], []).ShouldBe(
        [
            LuaValue.FromString("=(reader)"),
            LuaValue.FromInteger(-1)
        ]);

        var fromFile = InvokeBaseFunction(
            state,
            loadfile,
            LuaValue.FromString("fixture.luac"),
            LuaValue.FromString("b"),
            LuaValue.FromInteger(42));
        fromFile.Length.ShouldBe(1);
        state.InvokeCallable(fromFile[0], []).ShouldBe(
        [
            LuaValue.FromString("fixture.luac"),
            LuaValue.FromInteger(42)
        ]);

        InvokeBaseFunction(state, dofile, LuaValue.FromString("fixture.luac")).ShouldBe(
        [
            LuaValue.FromString("fixture.luac"),
            LuaValue.FromInteger(-1)
        ]);

        loaded.Count.ShouldBe(4);
        loaded[0].ChunkName.ShouldBe("=(string)");
        loaded[0].HasEnvironment.ShouldBeTrue();
        loaded[0].Environment.AsInteger().ShouldBe(41);
        loaded[1].ChunkName.ShouldBe("=(reader)");
        loaded[1].HasEnvironment.ShouldBeFalse();
        loaded[2].ChunkName.ShouldBe("fixture.luac");
        loaded[2].HasEnvironment.ShouldBeTrue();
        loaded[2].Environment.AsInteger().ShouldBe(42);
        loaded[3].ChunkName.ShouldBe("fixture.luac");
        loaded[3].HasEnvironment.ShouldBeFalse();
        loaded.ShouldAllBe(item => item.Bytes.SequenceEqual(chunkBytes));
    }

    [Fact]
    public void LoadAndLoadFile_ShouldReportUnsupportedTextAndFileErrors()
    {
        var state = new LuaState();
        var load = GetBaseFunction(state, "load");
        var loadfile = GetBaseFunction(state, "loadfile");
        var dofile = GetBaseFunction(state, "dofile");
        var binaryChunk = Encoding.Latin1.GetString(new byte[] { 0x1B, (byte)'L', (byte)'u', (byte)'a' });

        InvokeBaseFunction(state, load, LuaValue.FromString("return 1"))
            .ShouldBe(
            [
                LuaValue.Nil,
                LuaValue.FromString("text chunks are not supported yet")
            ]);

        InvokeBaseFunction(state, load, LuaValue.FromString(binaryChunk), LuaValue.Nil, LuaValue.FromString("t"))[0]
            .IsNil.ShouldBeTrue();
        InvokeBaseFunction(state, load, LuaValue.FromString(binaryChunk), LuaValue.Nil, LuaValue.FromString("t"))[1]
            .AsString().ShouldContain("binary chunk");

        state.FileReader = _ => throw new FileNotFoundException("missing");

        var loadFileResults = InvokeBaseFunction(state, loadfile, LuaValue.FromString("missing.lua"));
        loadFileResults[0].IsNil.ShouldBeTrue();
        loadFileResults[1].AsString().ShouldContain("cannot open missing.lua");

        var exception = Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, dofile, LuaValue.FromString("missing.lua")));
        exception.ErrorObject.AsString().ShouldContain("cannot open missing.lua");
    }

    [Fact]
    public void CollectGarbage_ShouldUseMinimalSemantics()
    {
        var state = new LuaState();
        var collectgarbage = GetBaseFunction(state, "collectgarbage");

        InvokeBaseFunction(state, collectgarbage, LuaValue.FromString("count"))
            .ShouldHaveSingleItem()
            .AsFloat().ShouldBeGreaterThan(0d);
        InvokeBaseFunction(state, collectgarbage, LuaValue.FromString("isrunning"))
            .ShouldHaveSingleItem()
            .AsBoolean().ShouldBeTrue();
        InvokeBaseFunction(state, collectgarbage, LuaValue.FromString("stop"))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(0);
        InvokeBaseFunction(state, collectgarbage, LuaValue.FromString("isrunning"))
            .ShouldHaveSingleItem()
            .AsBoolean().ShouldBeFalse();
        InvokeBaseFunction(state, collectgarbage, LuaValue.FromString("step"), LuaValue.FromInteger(4))
            .ShouldHaveSingleItem()
            .AsBoolean().ShouldBeFalse();
        InvokeBaseFunction(state, collectgarbage, LuaValue.FromString("collect"))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(0);
        InvokeBaseFunction(state, collectgarbage, LuaValue.FromString("restart"))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(0);
        InvokeBaseFunction(state, collectgarbage, LuaValue.FromString("isrunning"))
            .ShouldHaveSingleItem()
            .AsBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Require_ShouldUseMinimalPackageSemantics()
    {
        var state = new LuaState();
        state.SetCallableInvoker((callable, arguments) =>
        {
            var closure = callable.AsFunction();
            var body = (LuaNativeClosureBody)closure.Body!;
            return body.Function(state, closure, arguments);
        });

        var require = GetBaseFunction(state, "require");
        var preloadHits = 0;
        var fileHits = 0;
        var chunkBytes = new byte[] { 0x1B, (byte)'L', (byte)'u', (byte)'a', 0x01, 0x02 };

        state.PackagePreload.SetValue(
            LuaValue.FromString("pre_mod"),
            LuaValue.FromFunction(new LuaClosure(
                "pre_mod_loader",
                body: new LuaNativeClosureBody((_, _, arguments) =>
                {
                    preloadHits += 1;
                    var module = new LuaTable();
                    module.SetValue(LuaValue.FromString("name"), arguments[0]);
                    module.SetValue(LuaValue.FromString("loader"), arguments[1]);
                    return [LuaValue.FromTable(module)];
                }))));
        state.PackagePreload.SetValue(
            LuaValue.FromString("pre_true"),
            LuaValue.FromFunction(new LuaClosure(
                "pre_true_loader",
                body: new LuaNativeClosureBody((_, _, _) =>
                {
                    preloadHits += 1;
                    return [];
                }))));

        state.PackageLibrary.SetValue(
            LuaValue.FromString("path"),
            LuaValue.FromString("./missing/?.luac;./mods/?.luac"));
        state.FileReader = path =>
        {
            if (path == "./mods/file_mod.luac")
            {
                return chunkBytes;
            }

            throw new FileNotFoundException("missing");
        };
        state.SetBinaryChunkLoader((bytes, chunkName, hasEnvironment, environment) =>
        {
            fileHits += 1;
            bytes.ToArray().ShouldBe(chunkBytes);
            return new LuaClosure(
                chunkName,
                body: new LuaNativeClosureBody(static (_, _, _) => [LuaValue.FromInteger(77)]));
        });

        var preloaded = InvokeBaseFunction(state, require, LuaValue.FromString("pre_mod"));
        preloaded.Length.ShouldBe(2);
        preloaded[0].AsTable().GetValue(LuaValue.FromString("name")).AsString().ShouldBe("pre_mod");
        preloaded[0].AsTable().GetValue(LuaValue.FromString("loader")).AsString().ShouldBe(":preload:");
        preloaded[1].AsString().ShouldBe(":preload:");

        var cachedPreload = InvokeBaseFunction(state, require, LuaValue.FromString("pre_mod"));
        cachedPreload.Length.ShouldBe(1);
        cachedPreload[0].AsTable().ShouldBeSameAs(preloaded[0].AsTable());

        var fileLoaded = InvokeBaseFunction(state, require, LuaValue.FromString("file_mod"));
        fileLoaded.Length.ShouldBe(2);
        fileLoaded[0].AsInteger().ShouldBe(77);
        fileLoaded[1].AsString().ShouldBe("./mods/file_mod.luac");

        var cachedFile = InvokeBaseFunction(state, require, LuaValue.FromString("file_mod"));
        cachedFile.Length.ShouldBe(1);
        cachedFile[0].AsInteger().ShouldBe(77);

        var preTrue = InvokeBaseFunction(state, require, LuaValue.FromString("pre_true"));
        preTrue.Length.ShouldBe(2);
        preTrue[0].AsBoolean().ShouldBeTrue();
        preTrue[1].AsString().ShouldBe(":preload:");
        InvokeBaseFunction(state, require, LuaValue.FromString("pre_true"))
            .ShouldHaveSingleItem()
            .AsBoolean().ShouldBeTrue();

        preloadHits.ShouldBe(2);
        fileHits.ShouldBe(1);
    }

    [Fact]
    public void Require_ShouldReportMissingModules()
    {
        var state = new LuaState();
        state.PackageLibrary.SetValue(
            LuaValue.FromString("path"),
            LuaValue.FromString("./missing/?.luac;./mods/?.luac"));
        state.FileReader = _ => throw new FileNotFoundException("missing");
        var require = GetBaseFunction(state, "require");

        var exception = Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, require, LuaValue.FromString("missing.mod")));

        exception.ErrorObject.AsString().ShouldContain("module 'missing.mod' not found:");
        exception.ErrorObject.AsString().ShouldContain("no field package.preload['missing.mod']");
        exception.ErrorObject.AsString().ShouldContain("no file './missing/missing/mod.luac'");
        exception.ErrorObject.AsString().ShouldContain("no file './mods/missing/mod.luac'");
    }

    [Fact]
    public void StringLibraryAndMetamethods_ShouldUseLuaSemantics()
    {
        var state = new LuaState();
        state.SetCallableInvoker((callable, arguments) =>
        {
            var closure = callable.AsFunction();
            var body = (LuaNativeClosureBody)closure.Body!;
            return body.Function(state, closure, arguments);
        });

        var upper = state.StringLibrary.GetValue(LuaValue.FromString("upper"));
        var lower = state.StringLibrary.GetValue(LuaValue.FromString("lower"));
        var len = state.StringLibrary.GetValue(LuaValue.FromString("len"));

        state.InvokeCallable(upper, [LuaValue.FromString("lua")])
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("LUA");
        state.InvokeCallable(lower, [LuaValue.FromString("NeT")])
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("net");
        state.InvokeCallable(len, [LuaValue.FromString("lua")])
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(3);

        state.TryGetMetamethod(LuaValue.FromString("10"), "__add", out var add).ShouldBeTrue();
        state.TryGetMetamethod(LuaValue.FromString("7"), "__div", out var divide).ShouldBeTrue();
        state.TryGetMetamethod(LuaValue.FromString("7"), "__idiv", out var integerDivide).ShouldBeTrue();
        state.TryGetMetamethod(LuaValue.FromString("5"), "__unm", out var unaryMinus).ShouldBeTrue();

        state.InvokeCallable(add, [LuaValue.FromString("10"), LuaValue.FromInteger(1)])
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(11);
        state.InvokeCallable(divide, [LuaValue.FromString("7"), LuaValue.FromInteger(2)])
            .ShouldHaveSingleItem()
            .AsFloat().ShouldBe(3.5d);
        state.InvokeCallable(integerDivide, [LuaValue.FromString("7"), LuaValue.FromInteger(2)])
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(3);
        state.InvokeCallable(unaryMinus, [LuaValue.FromString("5")])
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(-5);

        var fallbackTable = new LuaTable();
        var fallbackMetatable = new LuaTable();
        fallbackMetatable.SetValue(
            LuaValue.FromString("__add"),
            LuaValue.FromFunction(new LuaClosure(
                "__add",
                body: new LuaNativeClosureBody(static (_, _, _) => [LuaValue.FromString("fallback")]))));
        fallbackTable.SetMetatable(fallbackMetatable);

        state.InvokeCallable(add, [LuaValue.FromString("x"), LuaValue.FromTable(fallbackTable)])
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("fallback");
    }

    [Fact]
    public void Warn_ShouldRejectNonStringLikeValues()
    {
        var state = new LuaState();
        var warn = GetBaseFunction(state, "warn");

        var exception = Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, warn, LuaValue.FromBoolean(true)));

        exception.ErrorObject.AsString().ShouldBe("bad argument #1 to 'warn' (string expected, got boolean)");
    }

    [Fact]
    public void PCall_ShouldReturnStatusAndResults()
    {
        var state = new LuaState();
        state.SetCallableInvoker((callable, arguments) =>
        {
            var closure = callable.AsFunction();
            var body = (LuaNativeClosureBody)closure.Body!;
            return body.Function(state, closure, arguments);
        });

        var pcall = GetBaseFunction(state, "pcall");
        var okClosure = new LuaClosure(
            "ok",
            body: new LuaNativeClosureBody(static (_, _, _) => [LuaValue.FromInteger(41), LuaValue.FromInteger(42)]));
        var errorClosure = new LuaClosure(
            "boom",
            body: new LuaNativeClosureBody(static (_, _, _) => throw new LuaRuntimeException(LuaValue.FromString("boom"))));

        var success = InvokeBaseFunction(state, pcall, LuaValue.FromFunction(okClosure));
        success.Length.ShouldBe(3);
        success[0].AsBoolean().ShouldBeTrue();
        success[1].AsInteger().ShouldBe(41);
        success[2].AsInteger().ShouldBe(42);

        var failure = InvokeBaseFunction(state, pcall, LuaValue.FromFunction(errorClosure));
        failure.Length.ShouldBe(2);
        failure[0].AsBoolean().ShouldBeFalse();
        failure[1].AsString().ShouldBe("boom");
    }

    [Fact]
    public void XPCall_ShouldTransformErrorsThroughMessageHandler()
    {
        var state = new LuaState();
        state.SetCallableInvoker((callable, arguments) =>
        {
            var closure = callable.AsFunction();
            var body = (LuaNativeClosureBody)closure.Body!;
            return body.Function(state, closure, arguments);
        });

        var xpcall = GetBaseFunction(state, "xpcall");
        var okClosure = new LuaClosure(
            "ok",
            body: new LuaNativeClosureBody(static (_, _, arguments) =>
            [
                LuaValue.FromInteger(arguments[0].AsInteger() + arguments[1].AsInteger()),
                LuaValue.FromInteger(arguments[0].AsInteger() * arguments[1].AsInteger())
            ]));
        var handlerClosure = new LuaClosure(
            "handler",
            body: new LuaNativeClosureBody(static (_, _, arguments) => [LuaValue.FromString("handled:" + arguments[0].AsString())]));
        var errorClosure = new LuaClosure(
            "boom",
            body: new LuaNativeClosureBody(static (_, _, _) => throw new LuaRuntimeException(LuaValue.FromString("boom"))));
        var badHandlerClosure = new LuaClosure(
            "bad-handler",
            body: new LuaNativeClosureBody(static (_, _, _) => throw new LuaRuntimeException(LuaValue.FromString("handler-boom"))));

        var success = InvokeBaseFunction(
            state,
            xpcall,
            LuaValue.FromFunction(okClosure),
            LuaValue.FromFunction(handlerClosure),
            LuaValue.FromInteger(6),
            LuaValue.FromInteger(7));
        success.Length.ShouldBe(3);
        success[0].AsBoolean().ShouldBeTrue();
        success[1].AsInteger().ShouldBe(13);
        success[2].AsInteger().ShouldBe(42);

        var failure = InvokeBaseFunction(
            state,
            xpcall,
            LuaValue.FromFunction(errorClosure),
            LuaValue.FromFunction(handlerClosure));
        failure.Length.ShouldBe(2);
        failure[0].AsBoolean().ShouldBeFalse();
        failure[1].AsString().ShouldBe("handled:boom");

        var handlerFailure = InvokeBaseFunction(
            state,
            xpcall,
            LuaValue.FromFunction(errorClosure),
            LuaValue.FromFunction(badHandlerClosure));
        handlerFailure.Length.ShouldBe(2);
        handlerFailure[0].AsBoolean().ShouldBeFalse();
        handlerFailure[1].AsString().ShouldBe("error in error handling");
    }

    [Fact]
    public void ToNumber_ShouldHandleStandardAndBaseConversions()
    {
        var state = new LuaState();
        var tonumber = GetBaseFunction(state, "tonumber");

        InvokeBaseFunction(state, tonumber, LuaValue.FromString(" 0x10 "))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(16);
        InvokeBaseFunction(state, tonumber, LuaValue.FromString("0x1.8p1"))
            .ShouldHaveSingleItem()
            .AsFloat().ShouldBe(3.0d);
        InvokeBaseFunction(state, tonumber, LuaValue.FromString("3.5"))
            .ShouldHaveSingleItem()
            .AsFloat().ShouldBe(3.5d);
        InvokeBaseFunction(state, tonumber, LuaValue.FromString("ff"), LuaValue.FromInteger(16))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(255);
        InvokeBaseFunction(state, tonumber, LuaValue.FromString("-10"), LuaValue.FromInteger(2))
            .ShouldHaveSingleItem()
            .AsInteger().ShouldBe(-2);
        InvokeBaseFunction(state, tonumber, LuaValue.FromString("19"), LuaValue.FromInteger(8))
            .ShouldHaveSingleItem()
            .IsNil.ShouldBeTrue();
        InvokeBaseFunction(state, tonumber, LuaValue.FromBoolean(true))
            .ShouldHaveSingleItem()
            .IsNil.ShouldBeTrue();
    }

    [Fact]
    public void ToString_ShouldRespectMetamethodsAndFallbackFormatting()
    {
        var state = new LuaState();
        state.SetCallableInvoker((callable, arguments) =>
        {
            var closure = callable.AsFunction();
            var body = (LuaNativeClosureBody)closure.Body!;
            return body.Function(state, closure, arguments);
        });

        var tostring = GetBaseFunction(state, "tostring");
        var namedTable = new LuaTable();
        var namedMetatable = new LuaTable();
        var customTable = new LuaTable();
        var customMetatable = new LuaTable();
        var badTable = new LuaTable();
        var badMetatable = new LuaTable();

        namedMetatable.SetValue(LuaValue.FromString("__name"), LuaValue.FromString("vec"));
        namedTable.SetMetatable(namedMetatable);

        customMetatable.SetValue(
            LuaValue.FromString("__tostring"),
            LuaValue.FromFunction(new LuaClosure(
                "__tostring",
                body: new LuaNativeClosureBody(static (_, _, _) => [LuaValue.FromString("custom")] ))));
        customTable.SetMetatable(customMetatable);

        badMetatable.SetValue(
            LuaValue.FromString("__tostring"),
            LuaValue.FromFunction(new LuaClosure(
                "__tostring",
                body: new LuaNativeClosureBody(static (_, _, _) => [LuaValue.FromInteger(42)] ))));
        badTable.SetMetatable(badMetatable);

        InvokeBaseFunction(state, tostring, LuaValue.FromFloat(3.0d))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("3.0");
        InvokeBaseFunction(state, tostring, LuaValue.FromTable(customTable))
            .ShouldHaveSingleItem()
            .AsString().ShouldBe("custom");

        var namedResult = InvokeBaseFunction(state, tostring, LuaValue.FromTable(namedTable))
            .ShouldHaveSingleItem()
            .AsString();
        namedResult.ShouldStartWith("vec: 0x");

        var functionResult = InvokeBaseFunction(
                state,
                tostring,
                LuaValue.FromFunction(new LuaClosure("demo")))
            .ShouldHaveSingleItem()
            .AsString();
        functionResult.ShouldStartWith("function: 0x");

        var exception = Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, tostring, LuaValue.FromTable(badTable)));
        exception.ErrorObject.AsString().ShouldBe("'__tostring' must return a string");
    }

    [Fact]
    public void CallFrame_ShouldAdvanceAndJump()
    {
        var frame = new CallFrame(new LuaClosure("main"), baseIndex: 0, expectedResults: 0);

        frame.Advance();
        frame.Advance(2);
        frame.ProgramCounter.ShouldBe(3);

        frame.Jump(7);
        frame.ProgramCounter.ShouldBe(7);
    }

    [Fact]
    public void CallFrame_ShouldCloseToBeClosedRegistersInDescendingOrder()
    {
        var frame = new CallFrame(new LuaClosure("main"), baseIndex: 0, expectedResults: 0);

        frame.RegisterToBeClosed(1);
        frame.RegisterToBeClosed(3);
        frame.RegisterToBeClosed(2);

        var closed = frame.ConsumeToBeClosedRegistersFrom(2);
        var remaining = frame.ConsumeToBeClosedRegistersFrom(0);

        closed.Count.ShouldBe(2);
        closed[0].ShouldBe(3);
        closed[1].ShouldBe(2);
        remaining.Count.ShouldBe(1);
        remaining[0].ShouldBe(1);
    }

    [Fact]
    public void CallFrame_ShouldExposeVarargsAndRegisterTop()
    {
        var frame = new CallFrame(
            new LuaClosure("main"),
            baseIndex: 0,
            expectedResults: 0,
            registerTop: 2,
            varargs:
            [
                LuaValue.FromInteger(10),
                LuaValue.FromInteger(20),
                LuaValue.FromInteger(30)
            ]);

        frame.RegisterTop.ShouldBe(2);
        frame.Varargs.Count.ShouldBe(3);
        frame.Varargs[1].AsInteger().ShouldBe(20);

        frame.SetRegisterTop(5);

        frame.RegisterTop.ShouldBe(5);
    }

    [Fact]
    public void Error_ShouldThrowLuaRuntimeException()
    {
        var state = new LuaState();
        var error = GetBaseFunction(state, "error");

        var exception = Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, error, LuaValue.FromString("test error")));

        exception.ErrorObject.AsString().ShouldBe("test error");
    }

    [Fact]
    public void Error_ShouldThrowNilWhenCalledWithNoArguments()
    {
        var state = new LuaState();
        var error = GetBaseFunction(state, "error");

        var exception = Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, error));

        exception.ErrorObject.IsNil.ShouldBeTrue();
    }

    [Fact]
    public void Select_ShouldRejectIndexZero()
    {
        var state = new LuaState();
        var select = GetBaseFunction(state, "select");

        var exception = Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, select, LuaValue.FromInteger(0), LuaValue.FromInteger(1)));

        exception.ErrorObject.AsString().ShouldContain("index out of range");
    }

    [Fact]
    public void Select_ShouldReturnEmptyForIndexBeyondArguments()
    {
        var state = new LuaState();
        var select = GetBaseFunction(state, "select");

        var results = InvokeBaseFunction(
            state, select, LuaValue.FromInteger(5),
            LuaValue.FromInteger(10), LuaValue.FromInteger(20));

        results.ShouldBeEmpty();
    }

    [Fact]
    public void ToNumber_ShouldRejectInvalidBase()
    {
        var state = new LuaState();
        var tonumber = GetBaseFunction(state, "tonumber");

        Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, tonumber, LuaValue.FromString("10"), LuaValue.FromInteger(1)));

        Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, tonumber, LuaValue.FromString("10"), LuaValue.FromInteger(37)));
    }

    [Fact]
    public void ToNumber_ShouldReturnNilForNonNumericStrings()
    {
        var state = new LuaState();
        var tonumber = GetBaseFunction(state, "tonumber");

        InvokeBaseFunction(state, tonumber, LuaValue.FromString("abc"))
            .ShouldHaveSingleItem()
            .IsNil.ShouldBeTrue();

        InvokeBaseFunction(state, tonumber, LuaValue.FromString(""))
            .ShouldHaveSingleItem()
            .IsNil.ShouldBeTrue();
    }

    [Fact]
    public void Assert_ShouldUseDefaultMessageWhenNoneProvided()
    {
        var state = new LuaState();
        var assert = GetBaseFunction(state, "assert");

        var exception = Should.Throw<LuaRuntimeException>(() =>
            InvokeBaseFunction(state, assert, LuaValue.Nil));

        exception.ErrorObject.AsString().ShouldBe("assertion failed!");
    }

    [Fact]
    public void PopFrame_ShouldRejectEmptyStack()
    {
        var state = new LuaState();

        Should.Throw<InvalidOperationException>(() => state.PopFrame());
    }

    private static LuaClosure GetBaseFunction(LuaState state, string name)
    {
        return state.GlobalEnvironment.GetValue(LuaValue.FromString(name)).AsFunction();
    }

    private static LuaClosure GetLibraryFunction(LuaTable table, string name)
    {
        return table.GetValue(LuaValue.FromString(name)).AsFunction();
    }

    private static LuaValue[] InvokeBaseFunction(LuaState state, LuaClosure closure, params LuaValue[] arguments)
    {
        return ((LuaNativeClosureBody)closure.Body!).Function(state, closure, arguments);
    }

    private static LuaValue[] InvokeClosure(LuaState state, LuaClosure closure, params LuaValue[] arguments)
    {
        return ((LuaNativeClosureBody)closure.Body!).Function(state, closure, arguments);
    }
}
