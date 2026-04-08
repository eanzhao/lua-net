using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
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
    public void CallFrame_ShouldAdvanceAndJump()
    {
        var frame = new CallFrame(new LuaClosure("main"), baseIndex: 0, expectedResults: 0);

        frame.Advance();
        frame.Advance(2);
        frame.ProgramCounter.ShouldBe(3);

        frame.Jump(7);
        frame.ProgramCounter.ShouldBe(7);
    }
}
