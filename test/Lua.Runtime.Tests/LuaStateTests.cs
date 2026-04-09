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
}
