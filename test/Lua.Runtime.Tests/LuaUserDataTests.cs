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
}
