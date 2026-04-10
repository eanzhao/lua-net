using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Shouldly;

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
    public void LuaState_ShouldPreloadMetatableAndRawHelpers()
    {
        var state = new LuaState();

        state.GlobalEnvironment.GetValue(LuaValue.FromString("getmetatable")).AsFunction().DebugName.ShouldBe("getmetatable");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("rawequal")).AsFunction().DebugName.ShouldBe("rawequal");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("rawlen")).AsFunction().DebugName.ShouldBe("rawlen");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("rawget")).AsFunction().DebugName.ShouldBe("rawget");
        state.GlobalEnvironment.GetValue(LuaValue.FromString("rawset")).AsFunction().DebugName.ShouldBe("rawset");
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
    public void CallFrame_ShouldTrackToBeClosedRegistersInReverseRegistrationOrder()
    {
        var frame = new CallFrame(new LuaClosure("main"), baseIndex: 0, expectedResults: 0);

        frame.RegisterToBeClosed(1);
        frame.RegisterToBeClosed(3);
        frame.RegisterToBeClosed(2);

        var closed = frame.ConsumeToBeClosedRegistersFrom(2);
        var remaining = frame.ConsumeToBeClosedRegistersFrom(0);

        closed.Count.ShouldBe(2);
        closed[0].ShouldBe(2);
        closed[1].ShouldBe(3);
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

    private static LuaClosure GetBaseFunction(LuaState state, string name)
    {
        return state.GlobalEnvironment.GetValue(LuaValue.FromString(name)).AsFunction();
    }

    private static LuaValue[] InvokeBaseFunction(LuaState state, LuaClosure closure, params LuaValue[] arguments)
    {
        return ((LuaNativeClosureBody)closure.Body!).Function(state, closure, arguments);
    }
}
