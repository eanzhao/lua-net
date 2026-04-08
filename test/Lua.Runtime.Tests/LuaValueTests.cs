using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Shouldly;

namespace Lua.Runtime.Tests;

public class LuaValueTests
{
    [Fact]
    public void Nil_ShouldExposeNilKind()
    {
        LuaValue.Nil.Kind.ShouldBe(LuaValueKind.Nil);
        LuaValue.Nil.IsNil.ShouldBeTrue();
    }

    [Fact]
    public void PrimitiveFactories_ShouldPreservePayload()
    {
        LuaValue.FromBoolean(true).AsBoolean().ShouldBeTrue();
        LuaValue.FromInteger(42).AsInteger().ShouldBe(42);
        LuaValue.FromFloat(3.5).AsFloat().ShouldBe(3.5);
        LuaValue.FromString("lua").AsString().ShouldBe("lua");
    }

    [Fact]
    public void Strings_ShouldUseValueEquality()
    {
        var left = LuaValue.FromString("runtime");
        var right = LuaValue.FromString(string.Concat("run", "time"));

        (left == right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void ReferenceValues_ShouldUseIdentityEquality()
    {
        var table = new LuaTable();
        var sameReference = LuaValue.FromTable(table);
        var anotherReference = LuaValue.FromTable(new LuaTable());

        (LuaValue.FromTable(table) == sameReference).ShouldBeTrue();
        (sameReference == anotherReference).ShouldBeFalse();
    }

    [Fact]
    public void AsBoolean_ShouldRejectWrongKind()
    {
        Should.Throw<InvalidOperationException>(() => LuaValue.FromInteger(1).AsBoolean());
    }

    [Fact]
    public void FunctionThreadAndUserData_ShouldRoundTrip()
    {
        var closure = new LuaClosure("main", 2);
        var thread = new LuaThread("main-thread");
        var userData = new LuaUserData(new object());

        LuaValue.FromFunction(closure).AsFunction().ShouldBeSameAs(closure);
        LuaValue.FromThread(thread).AsThread().ShouldBeSameAs(thread);
        LuaValue.FromUserData(userData).AsUserData().ShouldBeSameAs(userData);
    }
}
