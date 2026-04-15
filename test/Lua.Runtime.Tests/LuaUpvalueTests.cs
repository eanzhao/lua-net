using Lua.Runtime.Execution;
using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Shouldly;

namespace Lua.Runtime.Tests;

public class LuaUpvalueTests
{
    [Fact]
    public void OpenUpvalue_ShouldTrackStackAndCloseWithLastValue()
    {
        var state = new LuaState();
        state.Stack.SetTop(1);
        state.Stack[0] = LuaValue.FromInteger(40);

        var frame = new CallFrame(new LuaClosure("outer"), baseIndex: 0, expectedResults: 0);
        state.PushFrame(frame);

        var upvalue = frame.GetOrCreateOpenUpvalue(state, 0);
        upvalue.GetValue(state).AsInteger().ShouldBe(40);

        state.Stack[0] = LuaValue.FromInteger(41);
        upvalue.GetValue(state).AsInteger().ShouldBe(41);

        frame.CloseOpenUpvalues(state);
        state.Stack.SetTop(0);

        upvalue.GetValue(state).AsInteger().ShouldBe(41);

        upvalue.SetValue(state, LuaValue.FromInteger(42));
        upvalue.GetValue(state).AsInteger().ShouldBe(42);
    }
}
