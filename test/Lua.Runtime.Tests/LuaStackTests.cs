using Lua.Runtime.Execution;
using Lua.Runtime.Values;
using Shouldly;

namespace Lua.Runtime.Tests;

public class LuaStackTests
{
    [Fact]
    public void PushAndPop_ShouldBehaveAsLifo()
    {
        var stack = new LuaStack();
        stack.Push(LuaValue.FromInteger(1));
        stack.Push(LuaValue.FromInteger(2));

        stack.Count.ShouldBe(2);
        stack.Pop().AsInteger().ShouldBe(2);
        stack.Pop().AsInteger().ShouldBe(1);
        stack.Count.ShouldBe(0);
    }

    [Fact]
    public void Peek_ShouldSupportDepth()
    {
        var stack = new LuaStack();
        stack.Push(LuaValue.FromString("a"));
        stack.Push(LuaValue.FromString("b"));
        stack.Push(LuaValue.FromString("c"));

        stack.Peek().AsString().ShouldBe("c");
        stack.Peek(1).AsString().ShouldBe("b");
        stack.Peek(2).AsString().ShouldBe("a");
    }

    [Fact]
    public void SetTop_ShouldTrimOrPadWithNil()
    {
        var stack = new LuaStack();
        stack.Push(LuaValue.FromInteger(1));
        stack.Push(LuaValue.FromInteger(2));

        stack.SetTop(4);
        stack.Count.ShouldBe(4);
        stack[2].ShouldBe(LuaValue.Nil);
        stack[3].ShouldBe(LuaValue.Nil);

        stack.SetTop(1);
        stack.Count.ShouldBe(1);
        stack[0].AsInteger().ShouldBe(1);
    }

    [Fact]
    public void Pop_ShouldRejectEmptyStack()
    {
        var stack = new LuaStack();
        Should.Throw<InvalidOperationException>(() => stack.Pop());
    }
}
