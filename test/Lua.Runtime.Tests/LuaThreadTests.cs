using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Shouldly;

namespace Lua.Runtime.Tests;

public class LuaThreadTests
{
    [Fact]
    public void Constructor_ShouldPreserveDebugName()
    {
        var thread = new LuaThread("main");

        thread.DebugName.ShouldBe("main");
    }

    [Fact]
    public void Constructor_ShouldDefaultToNullDebugName()
    {
        var thread = new LuaThread();

        thread.DebugName.ShouldBeNull();
    }

    [Fact]
    public void LuaValue_ShouldRoundTripThread()
    {
        var thread = new LuaThread("test");
        var value = LuaValue.FromThread(thread);

        value.Kind.ShouldBe(LuaValueKind.Thread);
        value.AsThread().ShouldBeSameAs(thread);
    }

    [Fact]
    public void Threads_ShouldUseReferenceEquality()
    {
        var thread1 = new LuaThread("a");
        var thread2 = new LuaThread("a");

        var value1 = LuaValue.FromThread(thread1);
        var value2 = LuaValue.FromThread(thread2);

        value1.ShouldBe(LuaValue.FromThread(thread1));
        value1.ShouldNotBe(value2);
    }
}
