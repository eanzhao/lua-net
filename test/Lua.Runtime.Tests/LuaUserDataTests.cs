using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using Shouldly;

namespace Lua.Runtime.Tests;

public class LuaUserDataTests
{
    [Fact]
    public void SetMetatable_ShouldExposeMetamethodLookup()
    {
        var userData = new LuaUserData(new object());
        var metatable = new LuaTable();

        metatable.SetValue(LuaValue.FromString("__call"), LuaValue.FromString("handler"));
        userData.SetMetatable(metatable);

        userData.TryGetMetamethod("__call", out var handler).ShouldBeTrue();
        handler.AsString().ShouldBe("handler");
    }

    [Fact]
    public void SetMetatable_ShouldAllowClearingMetamethodLookup()
    {
        var userData = new LuaUserData(new object());
        var metatable = new LuaTable();

        metatable.SetValue(LuaValue.FromString("__len"), LuaValue.FromInteger(42));
        userData.SetMetatable(metatable);
        userData.SetMetatable(null);

        userData.TryGetMetamethod("__len", out _).ShouldBeFalse();
    }

    [Fact]
    public void UserValues_ShouldExposeConfiguredSlots()
    {
        var userData = new LuaUserData(new object(), userValueCount: 2);

        userData.UserValueCount.ShouldBe(2);
        userData.TryGetUserValue(1, out var first).ShouldBeTrue();
        first.IsNil.ShouldBeTrue();
        userData.TryGetUserValue(2, out var second).ShouldBeTrue();
        second.IsNil.ShouldBeTrue();
        userData.TryGetUserValue(3, out var missing).ShouldBeFalse();
        missing.IsNil.ShouldBeTrue();
    }

    [Fact]
    public void UserValues_ShouldAllowSettingExistingSlotsOnly()
    {
        var userData = new LuaUserData(new object(), userValueCount: 2);

        userData.TrySetUserValue(1, LuaValue.FromString("first")).ShouldBeTrue();
        userData.TrySetUserValue(2, LuaValue.FromInteger(42)).ShouldBeTrue();
        userData.TrySetUserValue(3, LuaValue.FromBoolean(true)).ShouldBeFalse();

        userData.TryGetUserValue(1, out var first).ShouldBeTrue();
        first.AsString().ShouldBe("first");
        userData.TryGetUserValue(2, out var second).ShouldBeTrue();
        second.AsInteger().ShouldBe(42);
    }
}
